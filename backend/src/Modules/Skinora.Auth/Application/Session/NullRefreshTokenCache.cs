namespace Skinora.Auth.Application.Session;

/// <summary>
/// No-op implementation used when Redis is not available (integration tests)
/// or intentionally disabled. Every <see cref="IRefreshTokenCache.GetAsync"/>
/// returns <c>null</c>, forcing the service layer to read the DB — which is
/// the documented fallback behaviour in 05 §6.1 ("Redis çökerse DB'den okunur").
/// </summary>
public sealed class NullRefreshTokenCache : IRefreshTokenCache
{
    public Task<RefreshTokenCacheEntry?> GetAsync(string tokenHash, CancellationToken cancellationToken)
        => Task.FromResult<RefreshTokenCacheEntry?>(null);

    public Task SetAsync(
        string tokenHash, RefreshTokenCacheEntry entry, TimeSpan ttl, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task RemoveAsync(string tokenHash, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
