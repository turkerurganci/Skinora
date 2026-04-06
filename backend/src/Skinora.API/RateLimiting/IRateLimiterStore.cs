namespace Skinora.API.RateLimiting;

/// <summary>
/// Storage abstraction for the fixed-window rate limiter.
/// Production: <see cref="RedisRateLimiterStore"/>.
/// Tests: <see cref="InMemoryRateLimiterStore"/>.
/// </summary>
public interface IRateLimiterStore
{
    /// <summary>
    /// Atomically increments the counter for a (policy, partitionKey) pair and
    /// reports whether the request is allowed within <paramref name="policy"/>.
    /// Implementations MUST guarantee that the counter and its TTL are set in a
    /// single round-trip so concurrent requests cannot race past the limit.
    /// </summary>
    Task<RateLimitResult> CheckAndIncrementAsync(
        string policyName,
        RateLimitPolicy policy,
        string partitionKey,
        CancellationToken cancellationToken);
}
