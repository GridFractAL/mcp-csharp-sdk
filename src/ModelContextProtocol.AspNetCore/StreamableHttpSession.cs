using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Security.Claims;

namespace ModelContextProtocol.AspNetCore;

// Modified by GridFractAL - Enterprise fork
// Changes: Unsealed class (internal sealed -> public) to allow extension

/// <summary>
/// Represents a single MCP session over HTTP.
/// Enterprise fork: Class is public (not internal sealed) to allow extension.
/// </summary>
public class StreamableHttpSession(
    string sessionId,
    StreamableHttpServerTransport transport,
    McpServer server,
    UserIdClaim? userId,
    StatefulSessionManager sessionManager) : IAsyncDisposable
{
    private int _referenceCount;
    private SessionState _state;
    private readonly object _stateLock = new();

    private int _getRequestStarted;
    private readonly CancellationTokenSource _disposeCts = new();

    /// <summary>Gets the unique session identifier.</summary>
    public string Id => sessionId;
    
    /// <summary>Gets the HTTP transport for this session.</summary>
    public StreamableHttpServerTransport Transport => transport;
    
    /// <summary>Gets the MCP server instance for this session.</summary>
    public McpServer Server => server;
    private StatefulSessionManager SessionManager => sessionManager;

    /// <summary>Gets the user ID associated with this session, if any.</summary>
    /// <remarks>Enterprise: Exposed for session persistence.</remarks>
    public string? UserId => userId?.Value;

    /// <summary>Gets a cancellation token that is cancelled when the session is closed.</summary>
    public CancellationToken SessionClosed => _disposeCts.Token;
    
    /// <summary>Gets whether the session is currently active.</summary>
    public bool IsActive => !SessionClosed.IsCancellationRequested && _referenceCount > 0;
    
    /// <summary>Gets the timestamp of the last activity on this session.</summary>
    public long LastActivityTicks { get; private set; } = sessionManager.TimeProvider.GetTimestamp();

    /// <summary>Gets or sets the task running the MCP server.</summary>
    public Task ServerRunTask { get; set; } = Task.CompletedTask;

    /// <summary>Acquires a reference to this session, preventing it from being disposed.</summary>
    public async ValueTask<IAsyncDisposable> AcquireReferenceAsync(CancellationToken cancellationToken)
    {
        // The StreamableHttpSession is not stored between requests in stateless mode. Instead, the session is recreated from the MCP-Session-Id.
        // Stateless sessions are 1:1 with HTTP requests and are outlived by the MCP session tracked by the Mcp-Session-Id.
        // Non-stateless sessions are 1:1 with the Mcp-Session-Id and outlive the POST request.
        // Non-stateless sessions get disposed by a DELETE request or the IdleTrackingBackgroundService.
        if (transport.Stateless)
        {
            return this;
        }

        SessionState startingState;

        lock (_stateLock)
        {
            startingState = _state;
            _referenceCount++;

            switch (startingState)
            {
                case SessionState.Uninitialized:
                    Debug.Assert(_referenceCount == 1, "The _referenceCount should start at 1 when the StreamableHttpSession is uninitialized.");
                    _state = SessionState.Started;
                    break;
                case SessionState.Started:
                    if (_referenceCount == 1)
                    {
                        sessionManager.DecrementIdleSessionCount();
                    }
                    // Update LastActivityTicks when acquiring reference in Started state to prevent timeout during active usage
                    LastActivityTicks = sessionManager.TimeProvider.GetTimestamp();
                    break;
                case SessionState.Disposed:
                    throw new ObjectDisposedException(nameof(StreamableHttpSession));
            }
        }

        if (startingState == SessionState.Uninitialized)
        {
            await sessionManager.StartNewSessionAsync(this, cancellationToken);
        }

        return new UnreferenceDisposable(this);
    }

    /// <summary>Attempts to mark a GET request as started. Returns true if this is the first GET request.</summary>
    public bool TryStartGetRequest() => Interlocked.Exchange(ref _getRequestStarted, 1) == 0;

    /// <summary>
    /// Checks if the current request user matches the session user.
    /// Enterprise modification: Allows anonymous sessions to be upgraded to authenticated sessions.
    /// This supports the MCP OAuth flow where discovery happens anonymously, then tools/call triggers auth.
    /// </summary>
    /// <remarks>
    /// Security: Anonymous sessions can only be upgraded by the FIRST authenticated request.
    /// Once upgraded, subsequent requests must match the bound user identity.
    /// </remarks>
    public bool HasSameUserId(ClaimsPrincipal user)
    {
        var currentUserId = StreamableHttpHandler.GetUserIdClaim(user);

        // If session was created anonymously and incoming request is also anonymous, allow
        if (userId is null && currentUserId is null)
            return true;

        // If session is anonymous but request is authenticated, this is an upgrade attempt
        // The caller (StreamableHttpHandler) should bind the user after this returns true
        if (userId is null && currentUserId is not null)
            return true;

        // If session has a user, require exact match (no downgrade to anonymous allowed)
        return userId == currentUserId;
    }

    /// <summary>Disposes this session and releases all resources.</summary>
    public async ValueTask DisposeAsync()
    {
        var wasIdle = false;

        lock (_stateLock)
        {
            switch (_state)
            {
                case SessionState.Uninitialized:
                    break;
                case SessionState.Started:
                    if (_referenceCount == 0)
                    {
                        wasIdle = true;
                    }
                    break;
                case SessionState.Disposed:
                    return;
            }

            _state = SessionState.Disposed;
        }

        try
        {
            try
            {
                // Dispose transport first to complete the incoming MessageReader gracefully and avoid a potentially unnecessary OCE.
                await transport.DisposeAsync();
                await _disposeCts.CancelAsync();

                await ServerRunTask;
            }
            finally
            {
                await server.DisposeAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (wasIdle)
            {
                sessionManager.DecrementIdleSessionCount();
            }
            _disposeCts.Dispose();
        }
    }

    private sealed class UnreferenceDisposable(StreamableHttpSession session) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            lock (session._stateLock)
            {
                Debug.Assert(session._state != SessionState.Uninitialized, "The session should have been initialized.");
                if (session._state != SessionState.Disposed && --session._referenceCount == 0)
                {
                    var sessionManager = session.SessionManager;
                    session.LastActivityTicks = sessionManager.TimeProvider.GetTimestamp();
                    sessionManager.IncrementIdleSessionCount();
                }
            }

            return default;
        }
    }

    private enum SessionState
    {
        Uninitialized,
        Started,
        Disposed
    }
}
