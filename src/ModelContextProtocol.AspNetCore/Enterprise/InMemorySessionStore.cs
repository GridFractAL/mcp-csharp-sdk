// Enterprise extension: Default in-memory session store (backward compatible)
// Copyright (c) GridFractAL - Enterprise fork of modelcontextprotocol/csharp-sdk
#pragma warning disable CS1591 // Missing XML comment - interface methods documented in ISessionStore
using System.Collections.Concurrent;

namespace ModelContextProtocol.AspNetCore.Enterprise;

/// <summary>
/// Default in-memory session store implementation.
///
/// This provides the same behavior as the upstream SDK's ConcurrentDictionary approach,
/// but implements the ISessionStore interface for consistency.
///
/// Limitations:
/// - Sessions are lost on server restart
/// - Not suitable for distributed deployments (use RedisSessionStore instead)
/// </summary>
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

        // Track user sessions
        if (!string.IsNullOrEmpty(metadata.UserId))
        {
            var userSessions = _userSessions.GetOrAdd(metadata.UserId, _ => new HashSet<string>());
            lock (userSessions)
            {
                userSessions.Add(sessionId);
            }
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
        if (_sessions.TryGetValue(sessionId, out var metadata))
        {
            metadata.LastActivityTicks = _timeProvider.GetTimestamp();
        }

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
        var idleTimeoutTicks = (long)(idleTimeout.TotalSeconds * TimeProvider.System.TimestampFrequency);
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
