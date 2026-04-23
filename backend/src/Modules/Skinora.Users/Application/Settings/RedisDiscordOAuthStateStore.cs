using StackExchange.Redis;

namespace Skinora.Users.Application.Settings;

public sealed class RedisDiscordOAuthStateStore : IDiscordOAuthStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    public RedisDiscordOAuthStateStore(IConnectionMultiplexer redis, string keyPrefix)
    {
        _redis = redis;
        _keyPrefix = string.IsNullOrWhiteSpace(keyPrefix) ? "skinora" : keyPrefix.TrimEnd(':');
    }

    public async Task IssueAsync(string state, Guid userId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(BuildKey(state), userId.ToString("N"), ttl);
    }

    public async Task<Guid?> ConsumeAsync(string state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;

        var db = _redis.GetDatabase();
        var value = await db.StringGetDeleteAsync(BuildKey(state));
        if (value.IsNullOrEmpty) return null;

        return Guid.TryParseExact(value.ToString(), "N", out var userId) ? userId : null;
    }

    private RedisKey BuildKey(string state)
        => $"{_keyPrefix}:settings:discord_state:{state}";
}
