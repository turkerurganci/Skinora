namespace Skinora.Shared.SteamMarket;

/// <summary>
/// Steam Market <c>priceoverview</c> API configuration (T81 — 08 §7.1–§7.4).
/// Bound from the <c>SteamMarket</c> section of <c>appsettings.json</c> or
/// the equivalent <c>SteamMarket__*</c> environment variables.
/// </summary>
/// <remarks>
/// The <see cref="Provider"/> switch picks between the real Steam Market
/// HTTP transport and a no-op logging stub so CI / fresh checkouts never
/// hammer the public endpoint by accident. Production must set
/// <see cref="Provider"/> to <c>steam-market</c>; CI and integration
/// tests leave it at <c>logging</c>.
///
/// <see cref="AppId"/> and <see cref="Currency"/> are pinned to CS2 / USD
/// in MVP (08 §7.2, 06 §3.24 "Sabitler"); they are exposed as config
/// knobs only to make the inevitable future migration to a multi-app or
/// multi-currency setup a one-line change rather than a deep refactor.
/// </remarks>
public sealed class SteamMarketSettings
{
    public const string SectionName = "SteamMarket";

    public const string ProviderSteamMarket = "steam-market";
    public const string ProviderLogging = "logging";

    /// <summary>
    /// Active provider — <c>steam-market</c> wires the real HTTP
    /// transport, <c>logging</c> short-circuits to a no-op stub that
    /// always reports "no price" so fraud checks degrade gracefully
    /// (08 §7.4 fallback). Defaults to <c>logging</c> so a fresh
    /// checkout never contacts steamcommunity.com.
    /// </summary>
    public string Provider { get; set; } = ProviderLogging;

    /// <summary>
    /// Steam Market base URL. Production points at
    /// <c>https://steamcommunity.com</c>; override only for offline
    /// integration test mirrors.
    /// </summary>
    public string BaseUrl { get; set; } = "https://steamcommunity.com";

    /// <summary>
    /// Steam app id — 730 = Counter-Strike 2 (08 §7.2). MVP single-app
    /// scope; column-less per 06 §3.24 "Sabitler".
    /// </summary>
    public int AppId { get; set; } = 730;

    /// <summary>
    /// Steam Market currency id — 1 = USD (08 §7.2). MVP single-currency
    /// scope; column-less per 06 §3.24 "Sabitler".
    /// </summary>
    public int Currency { get; set; } = 1;

    /// <summary>
    /// Per-request HTTP timeout. priceoverview is sub-second in the
    /// happy path; a 10-second budget mirrors T78 Resend / T79 Telegram
    /// / T80 Discord and keeps the fraud pipeline responsive without
    /// false-failing on flaky networks.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Sliding-window throughput cap for the Steam Market API
    /// (08 §7.1 — "~20 istek/dakika rate limit"). The client waits when
    /// the cap is hit rather than returning a transient error; cache
    /// hits short-circuit the limiter entirely.
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 20;

    /// <summary>
    /// Fresh window — cache entries fetched within this many hours are
    /// returned as-is without a background refresh (08 §7.3, 06 §3.24
    /// TTL semantiği).
    /// </summary>
    public int FreshTtlHours { get; set; } = 24;

    /// <summary>
    /// Stale-but-usable window — cache entries fetched between
    /// <see cref="FreshTtlHours"/> and <see cref="StaleTtlHours"/> are
    /// returned to the caller and a fire-and-forget background refresh
    /// is queued (08 §7.3 "stale" branch).
    /// </summary>
    public int StaleTtlHours { get; set; } = 48;
}
