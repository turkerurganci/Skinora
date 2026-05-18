using Skinora.Shared.Discord;
using StackExchange.Redis;

namespace Skinora.Notifications.Infrastructure.Channels;

/// <summary>
/// Redis-backed <see cref="IDiscordDmChannelCache"/>. Channel ids live
/// under <c>{prefix}:discord:dm_channel:{discordUserId}</c> with the
/// TTL supplied per write. Production deployments share the same
/// Redis instance with the other Skinora caches.
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>Skinora.Notifications</c> rather than
/// <c>Skinora.Shared.Discord</c> because <see cref="DiscordBotClient"/>
/// alone has no Redis dependency — the cache is only meaningful for
/// the notification send pipeline, so the Redis package reference
/// stays in this module (T78 / T79 precedent: Redis-backed adapters
/// live next to the consuming dispatcher).
/// </para>
/// </remarks>
public sealed class RedisDiscordDmChannelCache : IDiscordDmChannelCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    public RedisDiscordDmChannelCache(IConnectionMultiplexer redis, string keyPrefix)
    {
        _redis = redis;
        _keyPrefix = string.IsNullOrWhiteSpace(keyPrefix) ? "skinora" : keyPrefix.TrimEnd(':');
    }

    public async Task<string?> GetAsync(string discordUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(discordUserId)) return null;

        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(BuildKey(discordUserId));
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public async Task SetAsync(
        string discordUserId, string channelId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(discordUserId)
            || string.IsNullOrWhiteSpace(channelId))
        {
            return;
        }

        var db = _redis.GetDatabase();
        await db.StringSetAsync(BuildKey(discordUserId), channelId, ttl);
    }

    public async Task ForgetAsync(string discordUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(discordUserId)) return;

        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(BuildKey(discordUserId));
    }

    private RedisKey BuildKey(string discordUserId)
        => $"{_keyPrefix}:discord:dm_channel:{discordUserId}";
}
