// Enterprise extension: DI extensions for session store
// Copyright (c) GridFractAL - Enterprise fork of modelcontextprotocol/csharp-sdk

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace ModelContextProtocol.AspNetCore.Enterprise;

/// <summary>
/// Extension methods for registering session stores in dependency injection.
/// </summary>
public static class SessionStoreExtensions
{
    /// <summary>
    /// Add the default in-memory session store.
    /// This provides the same behavior as the upstream SDK.
    /// </summary>
    public static IServiceCollection AddMcpInMemorySessionStore(this IServiceCollection services)
    {
        services.AddSingleton<ISessionStore, InMemorySessionStore>();
        return services;
    }

    /// <summary>
    /// Add Redis-backed session store for distributed deployments.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configure">Configure Redis session store options</param>
    public static IServiceCollection AddMcpRedisSessionStore(
        this IServiceCollection services,
        Action<RedisSessionStoreOptions> configure)
    {
        var options = new RedisSessionStoreOptions();
        configure(options);

        // Register Redis connection if configuration string is provided
        if (!string.IsNullOrEmpty(options.Configuration))
        {
            services.AddSingleton<IConnectionMultiplexer>(sp =>
                ConnectionMultiplexer.Connect(options.Configuration));
        }

        services.AddSingleton<ISessionStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;

            return new RedisSessionStore(
                redis,
                options.KeyPrefix,
                options.DefaultTtl,
                options.ServerInstanceId,
                timeProvider);
        });

        return services;
    }

    /// <summary>
    /// Add Redis-backed session store using an existing IConnectionMultiplexer.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="redis">Existing Redis connection multiplexer</param>
    /// <param name="configure">Configure Redis session store options</param>
    public static IServiceCollection AddMcpRedisSessionStore(
        this IServiceCollection services,
        IConnectionMultiplexer redis,
        Action<RedisSessionStoreOptions>? configure = null)
    {
        var options = new RedisSessionStoreOptions();
        configure?.Invoke(options);

        services.AddSingleton<ISessionStore>(sp =>
        {
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;

            return new RedisSessionStore(
                redis,
                options.KeyPrefix,
                options.DefaultTtl,
                options.ServerInstanceId,
                timeProvider);
        });

        return services;
    }

    /// <summary>
    /// Add distributed cache-backed session store.
    /// Requires IDistributedCache to be registered (e.g., via AddDistributedRedisCache or AddDistributedSqlServerCache).
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configure">Configure distributed session store options</param>
    public static IServiceCollection AddMcpDistributedSessionStore(
        this IServiceCollection services,
        Action<DistributedSessionStoreOptions>? configure = null)
    {
        var options = new DistributedSessionStoreOptions();
        configure?.Invoke(options);

        services.AddSingleton<ISessionStore>(sp =>
        {
            var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
            var timeProvider = sp.GetService<TimeProvider>() ?? TimeProvider.System;

            return new DistributedSessionStore(
                cache,
                options.KeyPrefix,
                options.DefaultTtl,
                options.ServerInstanceId,
                timeProvider);
        });

        return services;
    }

    /// <summary>
    /// Add a custom session store implementation.
    /// </summary>
    public static IServiceCollection AddMcpSessionStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStore>(this IServiceCollection services)
        where TStore : class, ISessionStore
    {
        services.AddSingleton<ISessionStore, TStore>();
        return services;
    }
}

/// <summary>
/// Configuration options for Redis session store.
/// </summary>
public class RedisSessionStoreOptions
{
    /// <summary>
    /// Redis connection string (e.g., "localhost:6379" or "redis.example.com:6379,password=secret").
    /// If null, expects IConnectionMultiplexer to be registered in DI.
    /// </summary>
    public string? Configuration { get; set; }

    /// <summary>
    /// Prefix for Redis keys. Default: "mcp:session:"
    /// </summary>
    public string KeyPrefix { get; set; } = "mcp:session:";

    /// <summary>
    /// Default TTL for session keys. Default: 2 hours
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Unique identifier for this server instance.
    /// Default: MachineName:ProcessId
    /// </summary>
    public string? ServerInstanceId { get; set; }
}

/// <summary>
/// Configuration options for distributed cache session store.
/// </summary>
public class DistributedSessionStoreOptions
{
    /// <summary>
    /// Prefix for cache keys. Default: "mcp:session:"
    /// </summary>
    public string KeyPrefix { get; set; } = "mcp:session:";

    /// <summary>
    /// Default TTL for session keys. Default: 2 hours
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Unique identifier for this server instance.
    /// Default: MachineName:ProcessId
    /// </summary>
    public string? ServerInstanceId { get; set; }
}
