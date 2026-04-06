using StackExchange.Redis;

namespace Skinora.API.RateLimiting;

/// <summary>
/// Production rate-limit store backed by Redis. Uses a single atomic Lua
/// script per check so concurrent requests cannot race past the limit:
///
///   1. INCR the counter
///   2. If the new value == 1, set EXPIRE to the window length (first hit
///      of a new window)
///   3. Read PTTL to compute the precise reset time
///   4. Return [count, ttl_ms]
///
/// The script runs server-side in a single round-trip — there is no TOCTOU
/// gap between the increment and the expiry being applied.
/// </summary>
public sealed class RedisRateLimiterStore : IRateLimiterStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    // KEYS[1] = counter key
    // ARGV[1] = window length (seconds)
    // returns: { current_count, ttl_ms }
    private const string IncrementScript = @"
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        local ttl = redis.call('PTTL', KEYS[1])
        return { current, ttl }
    ";

    public RedisRateLimiterStore(IConnectionMultiplexer redis, string keyPrefix)
    {
        _redis = redis;
        _keyPrefix = keyPrefix;
    }

    public async Task<RateLimitResult> CheckAndIncrementAsync(
        string policyName,
        RateLimitPolicy policy,
        string partitionKey,
        CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var key = BuildKey(policyName, partitionKey);

        var raw = (RedisResult[])(await db.ScriptEvaluateAsync(
            IncrementScript,
            keys: [key],
            values: [policy.WindowSeconds]))!;

        var count = (long)raw[0];
        var ttlMs = (long)raw[1];

        // Defensive: if PTTL returned -1 (no expiry — shouldn't happen with the
        // script above) or -2 (key gone), fall back to a full window.
        if (ttlMs < 0)
        {
            ttlMs = policy.WindowSeconds * 1000L;
        }

        var resetAtUnixSeconds = DateTimeOffset.UtcNow.AddMilliseconds(ttlMs).ToUnixTimeSeconds();
        var remaining = (int)Math.Max(0, policy.Limit - count);
        var allowed = count <= policy.Limit;
        var retryAfterSeconds = allowed ? 0 : (int)Math.Ceiling(ttlMs / 1000.0);

        return new RateLimitResult(
            Allowed: allowed,
            Limit: policy.Limit,
            Remaining: remaining,
            ResetAtUnixSeconds: resetAtUnixSeconds,
            RetryAfterSeconds: retryAfterSeconds);
    }

    private string BuildKey(string policyName, string partitionKey)
        => $"{_keyPrefix}:{policyName}:{partitionKey}";
}
