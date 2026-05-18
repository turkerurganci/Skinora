using StackExchange.Redis;

namespace Skinora.Users.Application.Settings;

public sealed class RedisTelegramVerificationStore : ITelegramVerificationStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    public RedisTelegramVerificationStore(IConnectionMultiplexer redis, string keyPrefix)
    {
        _redis = redis;
        _keyPrefix = string.IsNullOrWhiteSpace(keyPrefix) ? "skinora" : keyPrefix.TrimEnd(':');
    }

    public async Task IssueAsync(string code, Guid userId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(BuildCodeKey(code), userId.ToString("N"), ttl);
    }

    public async Task<Guid?> ConsumeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var db = _redis.GetDatabase();
        var value = await db.StringGetDeleteAsync(BuildCodeKey(code));
        if (value.IsNullOrEmpty) return null;

        return Guid.TryParseExact(value.ToString(), "N", out var userId) ? userId : null;
    }

    public async Task<int> RegisterFailedAttemptAsync(
        long telegramUserId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var key = BuildFailKey(telegramUserId);
        var newValue = await db.StringIncrementAsync(key);
        if (newValue == 1)
        {
            // First increment in the window — bind the TTL so the
            // counter eventually resets without manual sweep.
            await db.KeyExpireAsync(key, ttl);
        }
        return (int)Math.Min(newValue, int.MaxValue);
    }

    public async Task<int> GetFailedAttemptsAsync(
        long telegramUserId, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(BuildFailKey(telegramUserId));
        if (value.IsNullOrEmpty) return 0;
        return int.TryParse(value.ToString(), out var count) ? count : 0;
    }

    private RedisKey BuildCodeKey(string code)
        => $"{_keyPrefix}:settings:tg_verify:{code}";

    private RedisKey BuildFailKey(long telegramUserId)
        => $"{_keyPrefix}:settings:tg_verify_fail:{telegramUserId}";
}
