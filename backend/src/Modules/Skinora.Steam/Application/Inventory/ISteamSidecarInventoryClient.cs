namespace Skinora.Steam.Application.Inventory;

/// <summary>
/// HTTP port over the Steam sidecar's inventory endpoints. The implementation
/// translates <see cref="SteamSidecarStatus"/> from raw HTTP status codes so
/// callers do not need to leak <c>HttpClient</c>/<c>HttpResponseMessage</c>
/// up the stack.
/// </summary>
public interface ISteamSidecarInventoryClient
{
    /// <summary>
    /// Fetch the inventory for <paramref name="steamId"/>. The sidecar handles
    /// pagination, assets+descriptions merge, and the 120-second Redis cache
    /// (08 §2.3) — callers see the already-shaped envelope.
    /// </summary>
    Task<SteamSidecarInventoryResult> GetInventoryAsync(
        string steamId, CancellationToken cancellationToken);

    /// <summary>
    /// Drop the sidecar's cached inventory for <paramref name="steamId"/>.
    /// Best-effort: implementations swallow transport errors and return
    /// <see cref="SteamSidecarStatus.Unavailable"/> so the caller can log
    /// without rolling back domain state.
    /// </summary>
    Task<SteamSidecarStatus> InvalidateInventoryAsync(
        string steamId, CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated outcome — distinguishes 200, 422 (private inventory) and
/// upstream failures without bubbling raw exceptions to the controller.
/// </summary>
public sealed record SteamSidecarInventoryResult(
    SteamSidecarStatus Status,
    SteamInventoryDto? Inventory);

public enum SteamSidecarStatus
{
    /// <summary>Sidecar returned a success envelope (HTTP 200 / 204).</summary>
    Success,

    /// <summary>The user's Steam profile/inventory is private (sidecar 422).</summary>
    InventoryPrivate,

    /// <summary>Sidecar 5xx, transport failure, or timeout — caller maps to 503 / falls back.</summary>
    Unavailable,
}
