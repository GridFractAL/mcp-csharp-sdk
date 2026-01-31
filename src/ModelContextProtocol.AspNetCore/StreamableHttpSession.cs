using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Security.Claims;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Represents a single MCP session over HTTP, managing session state, transport, and user binding.
/// </summary>
/// <remarks>
/// This class is unsealed to allow enterprise extensions (e.g., distributed session tracking).
/// Supports MCP OAuth flow where sessions can start anonymous and upgrade to authenticated.
/// </remarks>
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

    // Supports anonymous session upgrade to authenticated (MCP OAuth flow)
    private UserIdClaim? _boundUserId;

    /// <summary>
    /// Gets the unique session identifier.
    /// </summary>
    public string Id => sessionId;

    /// <summary>
    /// Gets the HTTP transport for this session.
    /// </summary>
    public StreamableHttpServerTransport Transport => transport;

    /// <summary>
    /// Gets the MCP server instance for this session.
    /// </summary>
    public McpServer Server => server;

    /// <summary>
    /// Gets the effective user ID for this session.
    /// Returns the bound user (from OAuth upgrade) if available, otherwise the original user.
    /// </summary>
    public UserIdClaim? UserId => Volatile.Read(ref _boundUserId) ?? userId;

    private StatefulSessionManager SessionManager => sessionManager;

    /// <summary>
    /// Gets a token that is canceled when the session is closed.
    /// </summary>
    public CancellationToken SessionClosed => _disposeCts.Token;

    /// <summary>
    /// Gets a value indicating whether the session is currently active.
    /// </summary>
    public bool IsActive => !SessionClosed.IsCancellationRequested && _referenceCount > 0;

    /// <summary>
    /// Gets the timestamp of the last activity on this session.
    /// </summary>
    public long LastActivityTicks { get; private set; } = sessionManager.TimeProvider.GetTimestamp();

    /// <summary>
    /// Gets or sets the task representing the server's run operation.
    /// </summary>
    public Task ServerRunTask { get; set; } = Task.CompletedTask;

    /// <summary>
    /// Acquires a reference to the session for the duration of a request.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An IAsyncDisposable that releases the reference when disposed.</returns>
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

    /// <summary>
    /// Attempts to start the GET request for this session. Only one GET request is allowed per session.
    /// </summary>
    /// <returns>true if this is the first GET request; otherwise, false.</returns>
    public bool TryStartGetRequest() => Interlocked.Exchange(ref _getRequestStarted, 1) == 0;

    /// <summary>
    /// Checks if the current request user matches the session user.
    /// Supports MCP OAuth flow where anonymous sessions upgrade to authenticated.
    /// </summary>
    /// <param name="user">The claims principal from the current request.</param>
    /// <returns>true if the user matches or the session upgrades successfully; otherwise, false.</returns>
    public bool HasSameUserId(ClaimsPrincipal user)
    {
        var currentUserId = StreamableHttpHandler.GetUserIdClaim(user);
        var effectiveUserId = Volatile.Read(ref _boundUserId) ?? userId;

        // Anonymous session + anonymous request = OK
        if (effectiveUserId is null && currentUserId is null)
            return true;

        // Anonymous session + authenticated request = upgrade attempt
        if (effectiveUserId is null && currentUserId is not null)
        {
            // Atomic upgrade - first authenticated request wins
            var winner = Interlocked.CompareExchange(ref _boundUserId, currentUserId, null);
            return winner is null || winner == currentUserId;
        }

        // Authenticated session requires exact match (no downgrade)
        return effectiveUserId == currentUserId;
    }

    /// <summary>
    /// Disposes the session, cleaning up transport, server, and tracking resources.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
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
