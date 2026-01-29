// Enterprise extension: Redis-backed session store for distributed deployments
// Copyright (c) GridFractAL - Enterprise fork of modelcontextprotocol/csharp-sdk

#pragma warning disable CS1591 // Missing XML comment - interface methods documented in ISessionStore

using System.Text.Json;
using StackExchange.Redis;

namespace ModelContextProtocol.AspNetCore.Enterprise;

/// <summary>
/// Redis-backed session store for distributed MCP server deployments.
///
/// Features:
/// - Session metadata persists across server restarts
/// - Multiple server instances can share session awareness
/// - Automatic TTL-based expiration
/// - User session indexing for multi-session lookup
///
/// Note: This stores session METADATA only. Live McpServer/transport objects
/// remain in-memory on the handling server. Redis enables:
/// - Session ID validation on reconnect
/// - Load balancer session affinity hints
/// - Graceful session recovery scenarios
/// </summary>
public class RedisSessionStore : ISessionStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeProvider _timeProvider;
    private readonly string _keyPrefix;
    private readonly TimeSpan _defaultTtl;
    private readonly string _serverInstanceId;

    /// <summary>
    /// Create Redis session store.
    /// </summary>
    /// <param name="redis">Redis connection multiplexer</param>
    /// <param name="keyPrefix">Prefix for Redis keys (default: "mcp:session:")</param>
    /// <param name="defaultTtl">Default TTL for session keys (default: 2 hours)</param>
    /// <param name="serverInstanceId">Unique identifier for this server instance</param>
    /// <param name="timeProvider">Time provider for timestamps</param>
    public RedisSessionStore(
        IConnectionMultiplexer redis,
        string keyPrefix = "mcp:session:",
        TimeSpan? defaultTtl = null,
        string? serverInstanceId = null,
        TimeProvider? timeProvider = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _keyPrefix = keyPrefix;
        _defaultTtl = defaultTtl ?? TimeSpan.FromHours(2);
        _serverInstanceId = serverInstanceId ?? Environment.MachineName + ":" + Environment.ProcessId;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private string SessionKey(string sessionId) => $"{_keyPrefix}{sessionId}";
    private string UserSessionsKey(string userId) => $"{_keyPrefix}user:{userId}";

    public async ValueTask<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        return await db.KeyExistsAsync(SessionKey(sessionId));
    }

    public async ValueTask<SessionMetadata?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var json = await db.StringGetAsync(SessionKey(sessionId));

        if (json.IsNullOrEmpty)
        {
            return null;
        }

        return JsonSerializer.Deserialize(json.ToString(), EnterpriseJsonContext.Default.SessionMetadata);
    }

    public async ValueTask SetAsync(string sessionId, SessionMetadata metadata, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = SessionKey(sessionId);

        // Set server instance ID
        var storedMetadata = metadata with { ServerInstanceId = _serverInstanceId };
        var json = JsonSerializer.Serialize(storedMetadata, EnterpriseJsonContext.Default.SessionMetadata);

        var transaction = db.CreateTransaction();

        // Store session metadata with TTL
        _ = transaction.StringSetAsync(key, json, _defaultTtl);

        // Add to user's session set if user ID is present
        if (!string.IsNullOrEmpty(metadata.UserId))
        {
            var userKey = UserSessionsKey(metadata.UserId);
            _ = transaction.SetAddAsync(userKey, sessionId);
            _ = transaction.KeyExpireAsync(userKey, _defaultTtl);
        }

        await transaction.ExecuteAsync();
    }

    public async ValueTask RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();

        // Get metadata first to find user ID
        var metadata = await GetAsync(sessionId, cancellationToken);

        var transaction = db.CreateTransaction();

        // Remove session key
        _ = transaction.KeyDeleteAsync(SessionKey(sessionId));

        // Remove from user's session set
        if (metadata?.UserId != null)
        {
            _ = transaction.SetRemoveAsync(UserSessionsKey(metadata.UserId), sessionId);
        }

        await transaction.ExecuteAsync();
    }

    public async ValueTask TouchAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = SessionKey(sessionId);

        // Get current metadata
        var json = await db.StringGetAsync(key);
        if (json.IsNullOrEmpty)
        {
            return;
        }

        var metadata = JsonSerializer.Deserialize(json.ToString(), EnterpriseJsonContext.Default.SessionMetadata);
        if (metadata == null)
        {
            return;
        }

        // Update last activity and refresh TTL
        var updatedMetadata = metadata with { LastActivityTicks = _timeProvider.GetTimestamp() };
        var updatedJson = JsonSerializer.Serialize(updatedMetadata, EnterpriseJsonContext.Default.SessionMetadata);

        await db.StringSetAsync(key, updatedJson, _defaultTtl);
    }

    public async ValueTask<IReadOnlyList<string>> GetUserSessionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var members = await db.SetMembersAsync(UserSessionsKey(userId));

        return members.Select(m => m.ToString()).ToList();
    }

    public async ValueTask<int> PruneExpiredAsync(TimeSpan idleTimeout, CancellationToken cancellationToken = default)
    {
        // Redis handles expiration automatically via TTL
        // This method is here for interface compliance and can be used
        // for additional cleanup logic if needed

        // For now, just return 0 - Redis TTL handles expiration
        return await ValueTask.FromResult(0);
    }

    /// <summary>
    /// Check if session is owned by this server instance.
    /// Useful for session recovery scenarios.
    /// </summary>
    public async ValueTask<bool> IsOwnedByThisServerAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var metadata = await GetAsync(sessionId, cancellationToken);
        return metadata?.ServerInstanceId == _serverInstanceId;
    }

    /// <summary>
    /// Get the server instance that owns a session.
    /// Returns null if session doesn't exist.
    /// </summary>
    public async ValueTask<string?> GetOwnerServerAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var metadata = await GetAsync(sessionId, cancellationToken);
        return metadata?.ServerInstanceId;
    }

    /// <summary>
    /// Transfer session ownership to this server.
    /// Used in session recovery when client reconnects to different server.
    /// </summary>
    public async ValueTask TransferOwnershipAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var metadata = await GetAsync(sessionId, cancellationToken);
        if (metadata != null)
        {
            var updated = metadata with { ServerInstanceId = _serverInstanceId };
            await SetAsync(sessionId, updated, cancellationToken);
        }
    }
}
