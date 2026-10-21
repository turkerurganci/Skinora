namespace Skinora.API.Services;

/// <summary>
/// Public (anonymous) read endpoints under <c>/platform</c>:
/// P1 stats (07 §10.1), P2 maintenance (07 §10.2) and P3 payout sources
/// (07 §10.3, T167).
/// </summary>
/// <remarks>
/// Lives at the API composition root because the stats payload spans
/// Transactions data while the maintenance payload reads from the Platform
/// SystemSettings catalog. Each method is cached behind <c>IMemoryCache</c>
/// per the contract TTLs (15 min / 30 sec).
/// </remarks>
public interface IPlatformPublicService
{
    Task<PlatformStatsResponse> GetStatsAsync(CancellationToken cancellationToken);

    Task<PlatformMaintenanceResponse> GetMaintenanceAsync(CancellationToken cancellationToken);

    /// <summary>
    /// P3 — the one address per network every seller payout leaves from
    /// (the network's hot wallet, 05 §3.3). Configuration, not data: read from
    /// <c>IPlatformWalletAddressProvider</c> on every call, no cache.
    /// </summary>
    PlatformPayoutSourcesResponse GetPayoutSources();
}
