using Microsoft.Extensions.Logging;
using Skinora.Transactions.Application.Steam;

namespace Skinora.Steam.Application.Inventory;

/// <summary>
/// Production implementation of <see cref="ISteamInventoryReader"/> (T67). The
/// stub fallback (<c>StubSteamInventoryReader</c>) is replaced via DI swap.
/// Looks the asset up in the sidecar's already-merged envelope (08 §2.3 →
/// 06 §3.5 columns).
/// </summary>
/// <remarks>
/// T121 — this is the seam where the sidecar's three-valued read used to be
/// collapsed onto a single <c>null</c>. The three
/// <see cref="SteamSidecarStatus"/> values now map one-to-one onto the 08 §2.3
/// visibilities, so "inventory read, asset absent" stays distinguishable from
/// "inventory hidden" and "Steam unreachable" all the way up to the caller.
/// </remarks>
public sealed class SidecarSteamInventoryReader : ISteamInventoryReader
{
    private readonly ISteamSidecarInventoryClient _sidecar;
    private readonly ILogger<SidecarSteamInventoryReader> _logger;

    public SidecarSteamInventoryReader(
        ISteamSidecarInventoryClient sidecar,
        ILogger<SidecarSteamInventoryReader> logger)
    {
        _sidecar = sidecar;
        _logger = logger;
    }

    public async Task<InventoryLookupResult> GetItemAsync(
        string steamId64,
        string itemAssetId,
        CancellationToken cancellationToken)
    {
        // A blank identifier is a caller bug, not a Steam answer. It must not
        // masquerade as evidence of absence, so it resolves to Unavailable
        // (no read happened) rather than NotFound.
        if (string.IsNullOrWhiteSpace(steamId64) || string.IsNullOrWhiteSpace(itemAssetId))
        {
            _logger.LogWarning(
                "Inventory lookup called with a blank steamId or assetId — treated as unreadable");
            return InventoryLookupResult.Unavailable;
        }

        var result = await _sidecar.GetInventoryAsync(steamId64, cancellationToken);
        switch (result.Status)
        {
            case SteamSidecarStatus.Success when result.Inventory is { } inv:
                var item = inv.Items.FirstOrDefault(it =>
                    string.Equals(it.AssetId, itemAssetId, StringComparison.Ordinal));
                if (item is null)
                {
                    _logger.LogInformation(
                        "Asset {AssetId} not present in {SteamId} inventory ({Total} items scanned)",
                        itemAssetId, steamId64, inv.TotalCount);
                    return InventoryLookupResult.NotFound;
                }
                return InventoryLookupResult.Found(new InventoryItemSnapshot(
                    AssetId: item.AssetId,
                    ClassId: item.ClassId,
                    InstanceId: item.InstanceId,
                    Name: item.Name,
                    // WP4a — carry the canonical Steam market key through to the
                    // fraud pre-check price lookup (08 §7.3). The sidecar already
                    // merged it from the item descriptions; it was previously
                    // dropped here, leaving PRICE_DEVIATION without a usable key.
                    MarketHashName: item.MarketHashName,
                    IconUrl: item.ImageUrl,
                    Exterior: item.Wear,
                    Type: item.Type,
                    // 08 §2.3 does not surface CS2 inspect links from the
                    // community endpoint — those come from a per-item action
                    // template that the T65 trade pipeline derives on send.
                    InspectLink: null,
                    IsTradeable: item.Tradeable));

            case SteamSidecarStatus.InventoryPrivate:
                _logger.LogInformation(
                    "Inventory for {SteamId} is private — no evidence either way", steamId64);
                return InventoryLookupResult.Private;

            case SteamSidecarStatus.Unavailable:
            default:
                _logger.LogWarning(
                    "Steam sidecar unavailable for {SteamId} — no evidence either way", steamId64);
                return InventoryLookupResult.Unavailable;
        }
    }
}
