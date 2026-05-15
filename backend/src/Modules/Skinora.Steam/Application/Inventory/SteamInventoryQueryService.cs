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
        var result = await _sidecar.GetInventoryAsync(steamId, cancellationToken);
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
