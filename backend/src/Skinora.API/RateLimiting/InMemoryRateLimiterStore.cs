using System.Collections.Concurrent;

namespace Skinora.API.RateLimiting;

/// <summary>
/// In-memory rate-limit store for tests and local development without Redis.
/// Implements the same fixed-window semantics as <see cref="RedisRateLimiterStore"/>:
/// the first request in a window seeds the expiry, subsequent requests within
/// the window share the original reset time.
///
/// Thread-safe via a per-key lock so concurrent requests cannot race past the
/// limit. Not suitable for multi-instance deployments — use the Redis store
/// in production.
/// </summary>
public sealed class InMemoryRateLimiterStore : IRateLimiterStore
{
    private readonly ConcurrentDictionary<string, Counter> _counters = new();
    private readonly Func<DateTimeOffset> _now;

    public InMemoryRateLimiterStore() : this(() => DateTimeOffset.UtcNow) { }

    /// <summary>Test seam: lets specs control the clock.</summary>
    public InMemoryRateLimiterStore(Func<DateTimeOffset> nowProvider)
    {
        _now = nowProvider;
    }

    public Task<RateLimitResult> CheckAndIncrementAsync(
        string policyName,
        RateLimitPolicy policy,
        string partitionKey,
        CancellationToken cancellationToken)
    {
        var key = $"{policyName}:{partitionKey}";
        var now = _now();

        var counter = _counters.GetOrAdd(key, _ => new Counter());

        long count;
        DateTimeOffset windowEnd;

        lock (counter)
        {
            // Window expired → start a new one
            if (counter.WindowEndUtc <= now)
            {
                counter.Count = 0;
                counter.WindowEndUtc = now.AddSeconds(policy.WindowSeconds);
            }

            counter.Count++;
            count = counter.Count;
            windowEnd = counter.WindowEndUtc;
        }

        var ttlSeconds = Math.Max(0, (int)Math.Ceiling((windowEnd - now).TotalSeconds));
        var allowed = count <= policy.Limit;
        var remaining = (int)Math.Max(0, policy.Limit - count);

        return Task.FromResult(new RateLimitResult(
            Allowed: allowed,
            Limit: policy.Limit,
            Remaining: remaining,
            ResetAtUnixSeconds: windowEnd.ToUnixTimeSeconds(),
            RetryAfterSeconds: allowed ? 0 : ttlSeconds));
    }

    /// <summary>Test helper: clear all counters between specs.</summary>
    public void Reset() => _counters.Clear();

    private sealed class Counter
    {
        public long Count;
        public DateTimeOffset WindowEndUtc;
    }
}
