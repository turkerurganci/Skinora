namespace Skinora.Shared.SteamMarket;

/// <summary>
/// Coordinates outbound requests to <c>steamcommunity.com/market/priceoverview</c>
/// against the ~20 req/dk soft cap documented in 08 §7.1. The cache
/// orchestrator only calls this when a fresh API hit is unavoidable
/// (expired / missing cache entry), so the limiter never sees cache
/// hits — it protects the egress side, not the read path.
/// </summary>
public interface ISteamMarketRateLimiter
{
    /// <summary>
    /// Blocks (asynchronously) until the next request can leave the
    /// process without exceeding the sliding-window quota. Records the
    /// outbound call timestamp on the way out so the next caller sees
    /// it.
    /// </summary>
    Task AcquireAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the limiter "cool down" for at least <paramref name="retryAfter"/>
    /// — invoked on a 429 response, an implicit throttle, or the
    /// "Steam Market down" branch in 08 §7.4. The next
    /// <see cref="AcquireAsync(CancellationToken)"/> waits past that
    /// deadline before issuing a new request.
    /// </summary>
    void RegisterRetryAfter(TimeSpan retryAfter);
}
