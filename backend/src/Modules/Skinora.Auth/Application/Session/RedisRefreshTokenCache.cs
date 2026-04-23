using System.Text.Json;
using StackExchange.Redis;

namespace Skinora.Auth.Application.Session;

/// <summary>
/// Redis-backed <see cref="IRefreshTokenCache"/> — 05 §6.1.
/// </summary>
/// <remarks>
/// Keys are namespaced under <c>{prefix}:refresh:</c>. Values are JSON-encoded
/// <see cref="RefreshTokenCacheEntry"/> payloads. TTL is set to the refresh
/// token's remaining lifetime so Redis eviction matches DB expiry semantics.
/// </remarks>
public sealed class RedisRefreshTokenCache : IRefreshTokenCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly string _keyPrefix;

    public RedisRefreshTokenCache(IConnectionMultiplexer redis, string keyPrefix)
    {
        _redis = redis;
        _keyPrefix = keyPrefix;
    }

    public async Task<RefreshTokenCacheEntry?> GetAsync(
        string tokenHash, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(BuildKey(tokenHash));
        if (value.IsNullOrEmpty) return null;

        try
        {
            return JsonSerializer.Deserialize<RefreshTokenCacheEntry>(value!, SerializerOptions);
        }
        catch (JsonException)
        {
            // Corrupt entry — drop it so the next call reads from the DB.
            await db.KeyDeleteAsync(BuildKey(tokenHash));
            return null;
        }
    }

    public Task SetAsync(
        string tokenHash, RefreshTokenCacheEntry entry, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (ttl <= TimeSpan.Zero) return Task.CompletedTask;

        var db = _redis.GetDatabase();
        var payload = JsonSerializer.Serialize(entry, SerializerOptions);
        return db.StringSetAsync(BuildKey(tokenHash), payload, ttl);
    }

    public Task RemoveAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        return db.KeyDeleteAsync(BuildKey(tokenHash));
    }

    private string BuildKey(string tokenHash) => $"{_keyPrefix}:refresh:{tokenHash}";
}
