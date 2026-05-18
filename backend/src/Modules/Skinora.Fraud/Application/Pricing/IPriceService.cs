namespace Skinora.Fraud.Application.Pricing;

/// <summary>
/// Cache-fronted Steam Market price lookup used by the fraud pipeline
/// for the price-deviation check (02 §14.1, 08 §7). The interface
/// returns a nullable decimal so the caller can treat "no price /
/// degraded API" uniformly per 08 §7.4 — null means "skip fraud check,
/// continue with other signals".
/// </summary>
public interface IPriceService
{
    /// <summary>
    /// Look up the canonical market price for <paramref name="marketHashName"/>.
    /// Honours the 08 §7.3 TTL stratejisi: cache-fresh returns
    /// immediately, cache-stale returns the cached value and queues a
    /// background refresh, cache-expired triggers a synchronous fetch.
    /// Returns <c>null</c> when Steam Market is unreachable / item has
    /// no price / cache + API both fail (08 §7.4 karar ağacı).
    /// </summary>
    Task<decimal?> GetMarketPriceAsync(string marketHashName, CancellationToken cancellationToken = default);
}
