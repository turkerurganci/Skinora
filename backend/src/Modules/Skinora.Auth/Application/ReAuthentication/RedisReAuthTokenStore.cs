using System.Text.Json;
using StackExchange.Redis;

namespace Skinora.Auth.Application.ReAuthentication;

/// <summary>
/// Redis-backed <see cref="IReAuthTokenStore"/>. Payload is serialized as JSON
/// under <c>{prefix}:reauth:token:{sha256}</c>. <see cref="ConsumeAsync"/> uses
/// <c>GETDEL</c> so read and invalidate are one round-trip and server-side
/// atomic — meeting the single-use contract under concurrent wallet-change
/// requests.
/// </summary>
public sealed class RedisReAuthTokenStore : IReAuthTokenStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    public RedisReAuthTokenStore(IConnectionMultiplexer redis, string keyPrefix)
    {
        _redis = redis;
        _keyPrefix = string.IsNullOrWhiteSpace(keyPrefix) ? "skinora" : keyPrefix.TrimEnd(':');
    }

    public async Task IssueAsync(
        string plainTextToken,
        ReAuthTokenPayload payload,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var key = BuildKey(plainTextToken);
        var json = JsonSerializer.Serialize(payload);
        await db.StringSetAsync(key, json, ttl);
    }

    public async Task<ReAuthTokenPayload?> ConsumeAsync(
        string plainTextToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(plainTextToken)) return null;

        var db = _redis.GetDatabase();
        var key = BuildKey(plainTextToken);

        var value = await db.StringGetDeleteAsync(key);
        if (value.IsNullOrEmpty) return null;

        try
        {
            return JsonSerializer.Deserialize<ReAuthTokenPayload>(value!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string BuildKey(string plainTextToken)
        => $"{_keyPrefix}:reauth:token:{ReAuthTokenHasher.Hash(plainTextToken)}";
}
