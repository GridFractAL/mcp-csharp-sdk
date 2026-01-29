// Enterprise extension: Session storage abstraction for distributed deployments
// Copyright (c) GridFractAL - Enterprise fork of modelcontextprotocol/csharp-sdk

namespace ModelContextProtocol.AspNetCore.Enterprise;

/// <summary>
/// Interface for MCP session storage, enabling distributed deployments.
///
/// The default upstream SDK uses in-memory ConcurrentDictionary, which loses sessions
/// on server restart. This interface enables implementations backed by Redis,
/// database, or other distributed storage systems.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Check if a session exists.
    /// </summary>
    ValueTask<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get session metadata.
    /// </summary>
    ValueTask<SessionMetadata?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store or update session metadata.
    /// </summary>
    ValueTask SetAsync(string sessionId, SessionMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a session.
    /// </summary>
    ValueTask RemoveAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Touch a session to update its last activity timestamp.
    /// </summary>
    ValueTask TouchAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all session IDs for a specific user.
    /// </summary>
    ValueTask<IReadOnlyList<string>> GetUserSessionsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prune sessions that have been idle longer than the specified timeout.
    /// Returns the number of sessions pruned.
    /// </summary>
    ValueTask<int> PruneExpiredAsync(TimeSpan idleTimeout, CancellationToken cancellationToken = default);
}

/// <summary>
/// Metadata stored for each MCP session.
/// </summary>
public record SessionMetadata
{
    /// <summary>
    /// Unique session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Optional user ID associated with this session.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Server instance that owns this session (for affinity hints).
    /// </summary>
    public string? ServerInstanceId { get; init; }

    /// <summary>
    /// When the session was created (ticks from TimeProvider).
    /// </summary>
    public long CreatedAtTicks { get; init; }

    /// <summary>
    /// Last activity timestamp (ticks from TimeProvider).
    /// </summary>
    public long LastActivityTicks { get; set; }

    /// <summary>
    /// Whether the session is currently active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Optional custom data for the session.
    /// </summary>
    public Dictionary<string, string>? CustomData { get; init; }
}
