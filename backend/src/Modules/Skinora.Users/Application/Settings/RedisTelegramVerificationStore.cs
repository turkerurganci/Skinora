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
        await db.StringSetAsync(BuildKey(code), userId.ToString("N"), ttl);
    }

    public async Task<Guid?> ConsumeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var db = _redis.GetDatabase();
        var value = await db.StringGetDeleteAsync(BuildKey(code));
        if (value.IsNullOrEmpty) return null;

        return Guid.TryParseExact(value.ToString(), "N", out var userId) ? userId : null;
    }

    private RedisKey BuildKey(string code)
        => $"{_keyPrefix}:settings:tg_verify:{code}";
}
