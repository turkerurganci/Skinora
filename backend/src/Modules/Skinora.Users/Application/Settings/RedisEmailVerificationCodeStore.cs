using System.Text.Json;
using StackExchange.Redis;

namespace Skinora.Users.Application.Settings;

/// <summary>
/// Redis-backed store for email verification codes. Mirrors the pattern in
/// <c>RedisReAuthTokenStore</c> (T31): keys are namespaced by
/// <c>{prefix}:settings:email_verify:{userId}</c> and
/// <c>{prefix}:settings:email_verify_cooldown:{userId}</c>.
/// </summary>
public sealed class RedisEmailVerificationCodeStore : IEmailVerificationCodeStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    public RedisEmailVerificationCodeStore(IConnectionMultiplexer redis, string keyPrefix)
    {
        _redis = redis;
        _keyPrefix = string.IsNullOrWhiteSpace(keyPrefix) ? "skinora" : keyPrefix.TrimEnd(':');
    }

    public async Task IssueAsync(
        Guid userId, string code, string emailAddress, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(new EmailVerificationCodeEntry(code, emailAddress));
        await db.StringSetAsync(CodeKey(userId), json, ttl);
    }

    public async Task<EmailVerificationCodeEntry?> PeekAsync(Guid userId, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(CodeKey(userId));
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<EmailVerificationCodeEntry>(value!);
    }

    public async Task<EmailVerificationCodeEntry?> ConsumeAsync(
        Guid userId, string candidate, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetDeleteAsync(CodeKey(userId));
        if (value.IsNullOrEmpty) return null;

        var entry = JsonSerializer.Deserialize<EmailVerificationCodeEntry>(value!);
        if (entry is null) return null;

        return string.Equals(entry.Code, candidate, StringComparison.Ordinal) ? entry : null;
    }

    public async Task<TimeSpan?> GetCooldownAsync(Guid userId, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var ttl = await db.KeyTimeToLiveAsync(CooldownKey(userId));
        return ttl;
    }

    public async Task RecordSendAsync(Guid userId, TimeSpan cooldown, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(CooldownKey(userId), "1", cooldown);
    }

    private RedisKey CodeKey(Guid userId)
        => $"{_keyPrefix}:settings:email_verify:{userId:N}";

    private RedisKey CooldownKey(Guid userId)
        => $"{_keyPrefix}:settings:email_verify_cooldown:{userId:N}";
}
