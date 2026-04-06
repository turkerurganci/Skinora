using StackExchange.Redis;

namespace Skinora.API.RateLimiting;

/// <summary>
/// DI registration helpers for the rate-limiting subsystem. Keeps Program.cs
/// thin and lets tests substitute the store via a single
/// <c>ConfigureTestServices</c> call.
/// </summary>
public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="RateLimitOptions"/>, opens a singleton
    /// <see cref="IConnectionMultiplexer"/> against the configured Redis
    /// instance and registers <see cref="RedisRateLimiterStore"/>.
    /// </summary>
    public static IServiceCollection AddRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));

        var redisConnectionString = configuration.GetSection("Redis")["ConnectionString"]
            ?? throw new InvalidOperationException(
                "Redis:ConnectionString is required for rate limiting.");

        // ConnectionMultiplexer is intentionally a singleton: it manages its
        // own connection pool and is the recommended StackExchange.Redis usage.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisConnectionString));

        services.AddSingleton<IRateLimiterStore>(sp =>
        {
            var redis = sp.GetRequiredService<IConnectionMultiplexer>();
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RateLimitOptions>>().Value;
            return new RedisRateLimiterStore(redis, options.KeyPrefix);
        });

        return services;
    }
}
