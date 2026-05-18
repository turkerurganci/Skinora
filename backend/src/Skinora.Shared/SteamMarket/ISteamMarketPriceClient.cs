namespace Skinora.Shared.SteamMarket;

/// <summary>
/// Transport-layer port for the Steam Market <c>priceoverview</c> API
/// (08 §7.2). Cache orchestration lives one layer up in the Fraud
/// module — this interface deliberately knows nothing about caching,
/// TTLs, or fraud thresholds.
/// </summary>
public interface ISteamMarketPriceClient
{
    /// <summary>
    /// Issue a single <c>priceoverview</c> call for <paramref name="marketHashName"/>.
    /// Returns one of three shapes (median / lowest / no-price); throws
    /// a <see cref="SteamMarketException"/> on transport failures so the
    /// cache orchestrator can map to "fiyat kontrolü atlanır" per
    /// 08 §7.4 karar ağacı.
    /// </summary>
    Task<SteamMarketPriceQuote> GetPriceAsync(string marketHashName, CancellationToken cancellationToken = default);
}
