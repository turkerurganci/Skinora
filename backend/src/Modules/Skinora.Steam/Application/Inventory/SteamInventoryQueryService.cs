namespace Skinora.Steam.Application.Inventory;

/// <summary>
/// Production implementation of <see cref="ISteamInventoryQueryService"/>.
/// Delegates to the sidecar HTTP client and maps the raw sidecar status into
/// the 07 §6.1 controller-facing status without leaking transport details.
/// </summary>
public sealed class SteamInventoryQueryService : ISteamInventoryQueryService
{
    private readonly ISteamSidecarInventoryClient _sidecar;

    public SteamInventoryQueryService(ISteamSidecarInventoryClient sidecar)
    {
        _sidecar = sidecar;
    }

    public async Task<GetInventoryResult> GetForSteamIdAsync(
        string steamId, CancellationToken cancellationToken)
    {
        // T123 — the 07 §6.1 listing surface keeps the cache. It is a browse
        // read that feeds an item picker, and it is what warms the entry the
        // create path then reuses; the transitions that must not be decided on
        // stale data ask for InventoryReadFreshness.Fresh instead.
        var result = await _sidecar.GetInventoryAsync(
            steamId, bypassCache: false, cancellationToken);
        return result.Status switch
        {
            SteamSidecarStatus.Success when result.Inventory is { } inv =>
                new GetInventoryResult(GetInventoryStatus.Success, inv),
            SteamSidecarStatus.InventoryPrivate =>
                new GetInventoryResult(GetInventoryStatus.InventoryPrivate, Inventory: null),
            _ =>
                new GetInventoryResult(GetInventoryStatus.SteamUnavailable, Inventory: null),
        };
    }
}
