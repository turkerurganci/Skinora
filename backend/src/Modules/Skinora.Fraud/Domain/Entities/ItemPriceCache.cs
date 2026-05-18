using Skinora.Shared.Domain;

namespace Skinora.Fraud.Domain.Entities;

/// <summary>
/// On-demand Steam Market price cache for fraud price-deviation checks
/// (06 §3.24, 08 §7.3). Used by the fraud pipeline only — never
/// surfaced to end users (08 §7.1 "Kullanım amacı: yalnızca fraud
/// tespiti").
/// </summary>
/// <remarks>
/// Cache lifecycle (06 §3.24 / 08 §7.3 TTL semantiği):
/// <list type="bullet">
///   <item><description>≤24 h fresh — value used as-is.</description></item>
///   <item><description>24–48 h stale — value used; background refresh queued.</description></item>
///   <item><description>>48 h expired — API hit required; on failure the fraud check is skipped (08 §7.4 adım 3b).</description></item>
/// </list>
///
/// AppId and currency are pinned (730 / USD) per 06 §3.24 "Sabitler"
/// — the columns are intentionally absent.
/// </remarks>
public class ItemPriceCache : BaseEntity
{
    /// <summary>
    /// Steam Market canonical item name (raw, not URL-encoded — e.g.
    /// <c>AK-47 | Redline (Field-Tested)</c>). UQ — one cache row per
    /// item.
    /// </summary>
    public string MarketHashName { get; set; } = string.Empty;

    /// <summary>
    /// <c>median_price</c> parse sonucu (08 §7.2 priority 1). Null when
    /// Steam returned <c>success: true</c> but the field was missing /
    /// unparseable — negative caching (06 §3.24).
    /// </summary>
    public decimal? MedianPrice { get; set; }

    /// <summary>
    /// <c>lowest_price</c> parse sonucu (08 §7.2 priority 2 fallback).
    /// Null per the same rule as <see cref="MedianPrice"/>.
    /// </summary>
    public decimal? LowestPrice { get; set; }

    /// <summary>
    /// Timestamp of the last successful Steam Market API call — TTL
    /// calculation uses this field (06 §3.24, 08 §7.3).
    /// </summary>
    public DateTime FetchedAt { get; set; }

    /// <summary>
    /// Data source — <c>STEAM_MARKET</c> in MVP (08 §7.1). Reserved for
    /// the 08 §7.5 büyüme yolu where an alternate aggregator may
    /// populate the same cache.
    /// </summary>
    public string Source { get; set; } = ItemPriceSources.SteamMarket;
}

/// <summary>
/// Allowed values for <see cref="ItemPriceCache.Source"/> — enforced by
/// CHECK constraint in <c>ItemPriceCacheConfiguration</c>.
/// </summary>
public static class ItemPriceSources
{
    public const string SteamMarket = "STEAM_MARKET";
}
