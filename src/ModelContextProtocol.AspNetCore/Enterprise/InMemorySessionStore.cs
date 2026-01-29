// Enterprise extension: Default in-memory session store (backward compatible)
// Copyright (c) GridFractAL - Enterprise fork of modelcontextprotocol/csharp-sdk
#pragma warning disable CS1591 // Missing XML comment - interface methods documented in ISessionStore
using System.Collections.Concurrent;

namespace ModelContextProtocol.AspNetCore.Enterprise;

/// <summary>
/// Default in-memory session store implementation.
/// </summary>
/// <remarks>
/// <para>
/// This provides the same behavior as the upstream SDK's ConcurrentDictionary approach,
/// but implements the ISessionStore interface for consistency.
/// </para>
/// <para>
/// <strong>Thread Safety:</strong> All operations are thread-safe. The locking strategy
/// acquires at most ONE HashSet lock per operation, preventing deadlock scenarios.
/// No method acquires multiple locks simultaneously.
/// </para>
/// <para>
/// Limitations:
/// <list type="bullet">
/// <item>Sessions are lost on server restart</item>
/// <item>Not suitable for distributed deployments (use <see cref="RedisSessionStore"/> instead)</item>
/// </list>
/// </para>
/// </remarks>
public class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionMetadata> _sessions = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _userSessions = new();
    private readonly TimeProvider _timeProvider;

    public InMemorySessionStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_sessions.ContainsKey(sessionId));
    }

    public ValueTask<SessionMetadata?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryGetValue(sessionId, out var metadata);
        return ValueTask.FromResult(metadata);
    }

    public ValueTask SetAsync(string sessionId, SessionMetadata metadata, CancellationToken cancellationToken = default)
    {
        _sessions[sessionId] = metadata;

        // Track user sessions with proper synchronization
        if (!string.IsNullOrEmpty(metadata.UserId))
        {
            // Use AddOrUpdate to atomically get-or-create and add in one operation
            // Note: The addValueFactory creates a pre-populated HashSet which is immediately
            // stored in the dictionary. ConcurrentDictionary guarantees only one factory runs,
            // so the HashSet is safely initialized before any other thread can access it.
            _userSessions.AddOrUpdate(
                metadata.UserId,
                // Factory for new key: create HashSet already containing sessionId
                // This is atomic - ConcurrentDictionary ensures only one factory executes
                _ => new HashSet<string> { sessionId },
                // Factory for existing key: add sessionId under lock
                (_, existingSet) =>
                {
                    lock (existingSet)
                    {
                        existingSet.Add(sessionId);
                    }
                    return existingSet;
                });
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryRemove(sessionId, out var metadata) && !string.IsNullOrEmpty(metadata.UserId))
        {
            if (_userSessions.TryGetValue(metadata.UserId, out var userSessions))
            {
                lock (userSessions)
                {
                    userSessions.Remove(sessionId);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask TouchAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        // Retry loop to handle concurrent modifications via optimistic concurrency.
        // 3 retries chosen as balance between contention handling and avoiding spin:
        // - 1 retry handles most single-contention scenarios
        // - 2-3 retries handle brief bursts of concurrent updates
        // - Beyond 3 indicates sustained heavy contention (unlikely for touch operations)
        const int maxRetries = 3;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            if (!_sessions.TryGetValue(sessionId, out var metadata))
                return ValueTask.CompletedTask; // Session doesn't exist - nothing to touch
                
            var updated = metadata with { LastActivityTicks = _timeProvider.GetTimestamp() };
            if (_sessions.TryUpdate(sessionId, updated, metadata))
                return ValueTask.CompletedTask; // Success
                
            // metadata was concurrently modified, retry with fresh value
        }
        
        // Retries exhausted due to sustained contention. This is non-fatal:
        // - Session still exists and remains valid
        // - LastActivityTicks will be stale but session won't be incorrectly pruned
        //   (next successful request will update it)
        // - High contention on a single session implies active usage, so premature
        //   expiration is unlikely
        // Logging would be appropriate here if ILogger were injected.
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<string>> GetUserSessionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (_userSessions.TryGetValue(userId, out var userSessions))
        {
            lock (userSessions)
            {
                return ValueTask.FromResult<IReadOnlyList<string>>(userSessions.ToList());
            }
        }

        return ValueTask.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    public ValueTask<int> PruneExpiredAsync(TimeSpan idleTimeout, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetTimestamp();
        var idleTimeoutTicks = (long)(idleTimeout.TotalSeconds * _timeProvider.TimestampFrequency);
        var pruned = 0;

        foreach (var kvp in _sessions)
        {
            if (now - kvp.Value.LastActivityTicks > idleTimeoutTicks)
            {
                if (_sessions.TryRemove(kvp.Key, out var metadata))
                {
                    pruned++;

                    if (!string.IsNullOrEmpty(metadata.UserId) &&
                        _userSessions.TryGetValue(metadata.UserId, out var userSessions))
                    {
                        lock (userSessions)
                        {
                            userSessions.Remove(kvp.Key);
                        }
                    }
                }
            }
        }

        return ValueTask.FromResult(pruned);
    }
}
