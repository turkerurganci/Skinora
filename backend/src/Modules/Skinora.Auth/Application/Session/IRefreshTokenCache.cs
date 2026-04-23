namespace Skinora.Auth.Application.Session;

/// <summary>
/// Redis-backed cache of active refresh token metadata keyed by the token's
/// SHA-256 hash — 05 §6.1 ("DB source of truth, Redis cache").
/// </summary>
/// <remarks>
/// Only active, non-rotated, non-revoked entries live in the cache. When a
/// token is rotated or revoked it is <b>removed</b> from the cache (rather than
/// flagged); a cache miss forces a DB read that will see the authoritative
/// state. This keeps the cache a pure acceleration layer over the DB and makes
/// Redis outages fall back to correct-but-slower reads.
/// </remarks>
public interface IRefreshTokenCache
{
    Task<RefreshTokenCacheEntry?> GetAsync(string tokenHash, CancellationToken cancellationToken);

    Task SetAsync(
        string tokenHash, RefreshTokenCacheEntry entry, TimeSpan ttl, CancellationToken cancellationToken);

    Task RemoveAsync(string tokenHash, CancellationToken cancellationToken);
}

/// <summary>Active refresh token metadata safe to cache.</summary>
public sealed record RefreshTokenCacheEntry(
    Guid TokenId, Guid UserId, DateTime ExpiresAt);
