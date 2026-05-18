namespace Skinora.Shared.SteamMarket;

/// <summary>
/// Result of a single <c>priceoverview</c> call (08 §7.2). One of three
/// shapes — see the static factories:
/// <list type="bullet">
///   <item><description><see cref="Median"/> — <c>median_price</c> parsed (priority 1).</description></item>
///   <item><description><see cref="Lowest"/> — <c>median_price</c> missing / unparseable, fell back to <c>lowest_price</c> (priority 2).</description></item>
///   <item><description><see cref="NoPrice"/> — Steam returned <c>success: true</c> but both fields empty (08 §7.2 öncelik 3, fiyat kontrolü atlanır).</description></item>
/// </list>
///
/// The choice carries deliberately into the fraud pipeline: a no-price
/// row is cached (06 §3.24 negative caching) so subsequent fraud checks
/// for the same item don't hammer the rate-limited endpoint.
/// </summary>
public sealed record SteamMarketPriceQuote
{
    public decimal? MedianPrice { get; init; }

    public decimal? LowestPrice { get; init; }

    /// <summary>
    /// True when Steam reported <c>success: true</c> with both price
    /// fields empty / unparseable — fraud check skipped per 08 §7.4
    /// karar ağacı adım 3b.
    /// </summary>
    public bool IsNoPrice { get; init; }

    /// <summary>
    /// The canonical price the fraud pipeline should compare against —
    /// median when available, lowest as fallback, null for no-price.
    /// </summary>
    public decimal? EffectivePrice => MedianPrice ?? LowestPrice;

    public static SteamMarketPriceQuote Median(decimal median, decimal? lowest = null) =>
        new() { MedianPrice = median, LowestPrice = lowest, IsNoPrice = false };

    public static SteamMarketPriceQuote Lowest(decimal lowest) =>
        new() { MedianPrice = null, LowestPrice = lowest, IsNoPrice = false };

    public static SteamMarketPriceQuote NoPrice() =>
        new() { MedianPrice = null, LowestPrice = null, IsNoPrice = true };
}
