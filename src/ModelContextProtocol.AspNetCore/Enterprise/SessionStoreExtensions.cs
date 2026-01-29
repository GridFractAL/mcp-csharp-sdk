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
    /// <remarks>
    /// <para>
    /// <strong>Connection Lifecycle:</strong> Redis connection is established lazily on first use,
    /// not during application startup. This means connection failures won't surface until the
    /// first request attempts to use the session store.
    /// </para>
    /// <para>
    /// <strong>Startup Validation:</strong> To validate Redis connectivity at startup, either:
    /// <list type="bullet">
    /// <item>Register your own <see cref="IConnectionMultiplexer"/> before calling this method</item>
    /// <item>Use a health check to verify Redis connectivity during startup</item>
    /// <item>Call <c>GetRequiredService&lt;IConnectionMultiplexer&gt;()</c> in a hosted service</item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Resilience:</strong> The connection uses <c>AbortOnConnectFail=false</c>,
    /// allowing automatic retry on transient connection failures.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMcpRedisSessionStore(
        this IServiceCollection services,
        Action<RedisSessionStoreOptions> configure)
    {
        var options = new RedisSessionStoreOptions();
        configure(options);

        // Register Redis connection lazily if configuration string is provided
        if (!string.IsNullOrEmpty(options.Configuration))
        {
            // Use Lazy<T> pattern to defer connection until first use
            // and handle connection failures gracefully
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var config = ConfigurationOptions.Parse(options.Configuration);
                config.AbortOnConnectFail = false; // Allow retry on connection failure
                return ConnectionMultiplexer.Connect(config);
            });
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
