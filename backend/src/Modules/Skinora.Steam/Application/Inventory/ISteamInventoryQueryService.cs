namespace Skinora.Steam.Application.Inventory;

/// <summary>
/// Application-layer query service backing S1
/// (<c>GET /steam/inventory</c>, 07 §6.1). Wraps the raw sidecar client with
/// the controller-friendly <see cref="GetInventoryStatus"/> mapping.
/// </summary>
public interface ISteamInventoryQueryService
{
    Task<GetInventoryResult> GetForSteamIdAsync(
        string steamId, CancellationToken cancellationToken);
}

/// <summary>Discriminated result mapping cleanly to 07 §6.1 error contract.</summary>
public sealed record GetInventoryResult(
    GetInventoryStatus Status,
    SteamInventoryDto? Inventory);

public enum GetInventoryStatus
{
    Success,
    InventoryPrivate,
    SteamUnavailable,
}
