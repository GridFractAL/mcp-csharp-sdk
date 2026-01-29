// Enterprise extension: IDistributedCache-backed session store for distributed deployments
// Copyright (c) GridFractAL - Enterprise fork of modelcontextprotocol/csharp-sdk

#pragma warning disable CS1591 // Missing XML comment - interface methods documented in ISessionStore

using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace ModelContextProtocol.AspNetCore.Enterprise;

/// <summary>
/// IDistributedCache-backed session store for distributed MCP server deployments.
/// Works with Redis, SQL Server, or any IDistributedCache implementation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>IMPORTANT: Eventual Consistency</strong><br/>
/// This implementation uses eventual consistency for user session lists.
/// The IDistributedCache interface does not support atomic read-modify-write operations,
/// so concurrent modifications to user session lists may result in lost updates (last-write-wins).
/// </para>
/// <para>
/// The session metadata itself (GetAsync/SetAsync) is the authoritative source of truth.
/// User session lists (GetUserSessionsAsync) are an optimization and may be stale.
/// For high-concurrency production use, prefer <see cref="RedisSessionStore"/> which
/// uses atomic Redis SET operations.
/// </para>
/// <para>
/// Features:
/// <list type="bullet">
/// <item>Session metadata persists across server restarts</item>
/// <item>Multiple server instances can share session awareness</item>
/// <item>Automatic TTL-based expiration via IDistributedCache</item>
/// <item>User session indexing for multi-session lookup (eventual consistency)</item>
/// </list>
/// </para>
/// <para>
/// Note: This stores session METADATA only. Live McpServer/transport objects
/// remain in-memory on the handling server.
/// </para>
/// </remarks>
public class DistributedSessionStore : ISessionStore
{
    private readonly IDistributedCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly string _keyPrefix;
    private readonly TimeSpan _defaultTtl;
    private readonly string _serverInstanceId;

    /// <summary>
    /// Create distributed session store using IDistributedCache.
    /// </summary>
    /// <param name="cache">IDistributedCache implementation (Redis, SQL, Memory)</param>
    /// <param name="keyPrefix">Prefix for cache keys (default: "mcp:session:")</param>
    /// <param name="defaultTtl">Default TTL for session keys (default: 2 hours)</param>
    /// <param name="serverInstanceId">Unique identifier for this server instance</param>
    /// <param name="timeProvider">Time provider for timestamps</param>
    public DistributedSessionStore(
        IDistributedCache cache,
        string keyPrefix = "mcp:session:",
        TimeSpan? defaultTtl = null,
        string? serverInstanceId = null,
        TimeProvider? timeProvider = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _keyPrefix = keyPrefix;
        _defaultTtl = defaultTtl ?? TimeSpan.FromHours(2);
        _serverInstanceId = serverInstanceId ?? Environment.MachineName + ":" + Environment.ProcessId;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private string SessionKey(string sessionId) => $"{_keyPrefix}{sessionId}";
    private string UserSessionsKey(string userId) => $"{_keyPrefix}user:{userId}";

    public async ValueTask<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var json = await _cache.GetStringAsync(SessionKey(sessionId), cancellationToken);
        return !string.IsNullOrEmpty(json);
    }

    public async ValueTask<SessionMetadata?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var json = await _cache.GetStringAsync(SessionKey(sessionId), cancellationToken);

        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize(json, EnterpriseJsonContext.Default.SessionMetadata);
    }

    public async ValueTask SetAsync(string sessionId, SessionMetadata metadata, CancellationToken cancellationToken = default)
    {
        var key = SessionKey(sessionId);

        // Set server instance ID
        var storedMetadata = metadata with { ServerInstanceId = _serverInstanceId };
        var json = JsonSerializer.Serialize(storedMetadata, EnterpriseJsonContext.Default.SessionMetadata);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _defaultTtl
        };

        // Store session metadata with TTL
        await _cache.SetStringAsync(key, json, options, cancellationToken);

        // Add to user's session set if user ID is present
        if (!string.IsNullOrEmpty(metadata.UserId))
        {
            await AddToUserSessionsAsync(metadata.UserId, sessionId, cancellationToken);
        }
    }

    public async ValueTask RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        // Get metadata first to find user ID
        var metadata = await GetAsync(sessionId, cancellationToken);

        // Remove session key
        await _cache.RemoveAsync(SessionKey(sessionId), cancellationToken);

        // Remove from user's session set
        if (metadata?.UserId != null)
        {
            await RemoveFromUserSessionsAsync(metadata.UserId, sessionId, cancellationToken);
        }
    }

    public async ValueTask TouchAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var key = SessionKey(sessionId);

        // Get current metadata
        var json = await _cache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        var metadata = JsonSerializer.Deserialize(json, EnterpriseJsonContext.Default.SessionMetadata);
        if (metadata == null)
        {
            return;
        }

        // Update last activity and refresh TTL
        var updatedMetadata = metadata with { LastActivityTicks = _timeProvider.GetTimestamp() };
        var updatedJson = JsonSerializer.Serialize(updatedMetadata, EnterpriseJsonContext.Default.SessionMetadata);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _defaultTtl
        };

        await _cache.SetStringAsync(key, updatedJson, options, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<string>> GetUserSessionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var json = await _cache.GetStringAsync(UserSessionsKey(userId), cancellationToken);

        if (string.IsNullOrEmpty(json))
        {
            return Array.Empty<string>();
        }

        return JsonSerializer.Deserialize(json, EnterpriseJsonContext.Default.ListString) ?? new List<string>();
    }

    public ValueTask<int> PruneExpiredAsync(TimeSpan idleTimeout, CancellationToken cancellationToken = default)
    {
        // IDistributedCache handles expiration automatically via TTL
        // This method is here for interface compliance
        return ValueTask.FromResult(0);
    }

    private async Task AddToUserSessionsAsync(string userId, string sessionId, CancellationToken cancellationToken)
    {
        var userKey = UserSessionsKey(userId);
        
        // IMPORTANT: IDistributedCache doesn't support atomic operations.
        // This implementation accepts eventual consistency:
        // - Concurrent adds may result in lost updates (last-write-wins)
        // - The session metadata (GetAsync) is the source of truth
        // - User session lists are an optimization for lookup, not authoritative
        // 
        // For high-concurrency production use, prefer RedisSessionStore which
        // uses atomic Redis SET operations.
        
        var existingJson = await _cache.GetStringAsync(userKey, cancellationToken);

        var sessions = !string.IsNullOrEmpty(existingJson)
            ? JsonSerializer.Deserialize(existingJson, EnterpriseJsonContext.Default.ListString) ?? new List<string>()
            : new List<string>();

        if (sessions.Contains(sessionId))
            return; // Already added

        sessions.Add(sessionId);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _defaultTtl
        };

        await _cache.SetStringAsync(userKey, JsonSerializer.Serialize(sessions, EnterpriseJsonContext.Default.ListString), options, cancellationToken);
        // Note: No verification - accepting eventual consistency
    }

    private async Task RemoveFromUserSessionsAsync(string userId, string sessionId, CancellationToken cancellationToken)
    {
        var userKey = UserSessionsKey(userId);
        
        // Same eventual consistency model as AddToUserSessionsAsync.
        // Stale session IDs in the list are harmless - they point to
        // non-existent sessions and will be naturally cleaned up on TTL expiry.
        
        var existingJson = await _cache.GetStringAsync(userKey, cancellationToken);

        if (string.IsNullOrEmpty(existingJson))
            return;

        var sessions = JsonSerializer.Deserialize(existingJson, EnterpriseJsonContext.Default.ListString) ?? new List<string>();
        
        if (!sessions.Contains(sessionId))
            return; // Already removed
            
        sessions.Remove(sessionId);

        if (sessions.Count > 0)
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _defaultTtl
            };
            await _cache.SetStringAsync(userKey, JsonSerializer.Serialize(sessions, EnterpriseJsonContext.Default.ListString), options, cancellationToken);
        }
        else
        {
            await _cache.RemoveAsync(userKey, cancellationToken);
        }
        // Note: No verification - accepting eventual consistency
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
