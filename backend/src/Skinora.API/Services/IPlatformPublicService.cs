namespace Skinora.API.Services;

/// <summary>
/// Public (anonymous) read endpoints under <c>/platform</c>:
/// P1 stats (07 §10.1) and P2 maintenance (07 §10.2).
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
}
