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
        InventoryReadFreshness freshness,
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

        var result = await _sidecar.GetInventoryAsync(
            steamId64, BypassCache(freshness), cancellationToken);
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
                    IsTradeable: item.Tradeable)
                {
                    // T125 — audit material for the launch-gate capture only
                    // (DEPLOY_RUNBOOK §H). Nothing in the evidence engine reads
                    // it; 02 §9.2 decides on a class count delta.
                    AssetProperties = MapProperties(item),
                });

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

    /// <inheritdoc />
    public async Task<InventoryClassBaselineResult> CaptureClassBaselineAsync(
        string steamId64,
        string classId,
        string? instanceId,
        InventoryReadFreshness freshness,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(steamId64) || string.IsNullOrWhiteSpace(classId))
        {
            _logger.LogWarning(
                "Baseline capture called with a blank steamId or classId — treated as unreadable");
            return InventoryClassBaselineResult.Unavailable;
        }

        var result = await _sidecar.GetInventoryAsync(
            steamId64, BypassCache(freshness), cancellationToken);
        switch (result.Status)
        {
            case SteamSidecarStatus.Success when result.Inventory is { } inv:
                var assets = inv.Items
                    .Where(it => MatchesClass(it, classId, instanceId))
                    .Select(it => new InventoryClassAsset(it.AssetId, MapProperties(it)))
                    .ToList();
                // T130 — the inventory-wide class fingerprint rides along on the
                // same response. The envelope is already in hand; projecting it
                // here is what keeps the 06 §3.5 BuyerBaselineClassIds column
                // free of a second Fresh (cache-bypassing) Steam round trip.
                var inventoryClassIds = ProjectClassIds(inv);
                _logger.LogInformation(
                    "Baseline for {SteamId} class {ClassId}/{InstanceId}: {Count} copies "
                    + "of {Total} items scanned, {Classes} distinct classes fingerprinted",
                    steamId64, classId, instanceId ?? "-", assets.Count, inv.TotalCount,
                    inventoryClassIds.Count);
                return InventoryClassBaselineResult.Captured(assets, inventoryClassIds);

            case SteamSidecarStatus.InventoryPrivate:
                _logger.LogInformation(
                    "Inventory for {SteamId} is private — no delivery baseline (02 §9.2)", steamId64);
                return InventoryClassBaselineResult.Private;

            case SteamSidecarStatus.Unavailable:
            default:
                _logger.LogWarning(
                    "Steam sidecar unavailable for {SteamId} — no delivery baseline", steamId64);
                return InventoryClassBaselineResult.Unavailable;
        }
    }

    /// <inheritdoc />
    public async Task<InventoryFingerprintResult> CaptureInventoryFingerprintAsync(
        string steamId64,
        InventoryReadFreshness freshness,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(steamId64))
        {
            _logger.LogWarning(
                "Inventory fingerprint called with a blank steamId — treated as unreadable");
            return InventoryFingerprintResult.Unavailable;
        }

        var result = await _sidecar.GetInventoryAsync(
            steamId64, BypassCache(freshness), cancellationToken);
        switch (result.Status)
        {
            case SteamSidecarStatus.Success when result.Inventory is { } inv:
                var assets = inv.Items
                    .Select(it => new InventoryFingerprintEntry(
                        it.AssetId, it.ClassId, it.InstanceId, it.Name))
                    .ToList();
                _logger.LogInformation(
                    "Fingerprint for {SteamId}: {Count} assets over {Classes} distinct classes",
                    steamId64, assets.Count,
                    assets.Select(a => a.ClassId).Distinct(StringComparer.Ordinal).Count());
                return InventoryFingerprintResult.Captured(assets);

            case SteamSidecarStatus.InventoryPrivate:
                _logger.LogInformation(
                    "Inventory for {SteamId} is private — no fingerprint (03 §6.3)", steamId64);
                return InventoryFingerprintResult.Private;

            case SteamSidecarStatus.Unavailable:
            default:
                _logger.LogWarning(
                    "Steam sidecar unavailable for {SteamId} — no fingerprint", steamId64);
                return InventoryFingerprintResult.Unavailable;
        }
    }

    /// <summary>
    /// The distinct class ids of a whole inventory envelope — the 06 §3.5
    /// <c>BuyerBaselineClassIds</c> shape.
    /// </summary>
    private static IReadOnlyList<string> ProjectClassIds(SteamInventoryDto inventory) =>
    [
        .. inventory.Items
            .Select(it => it.ClassId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
    ];

    /// <summary>
    /// 06 §3.5 pairs <c>ClassId</c> with <c>InstanceId</c>, so both take part in
    /// the match. A null <paramref name="instanceId"/> on the transaction means
    /// the listing was created without one and matches on class alone — the
    /// alternative (requiring both sides to be null) would silently produce an
    /// empty baseline for every such listing, and an empty baseline reads as a
    /// claim rather than a gap.
    /// </summary>
    private static bool MatchesClass(
        SteamInventoryItemDto item, string classId, string? instanceId)
    {
        if (!string.Equals(item.ClassId, classId, StringComparison.Ordinal))
            return false;
        return instanceId is null
            || string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal);
    }

    /// <summary>
    /// T125 — carry Steam's per-asset <c>asset_properties</c> across the module
    /// boundary (T122 runbook §5). Pure projection; no field here is ever an
    /// input to the 02 §9.2 delivery decision.
    /// </summary>
    private static IReadOnlyList<InventoryAssetProperty> MapProperties(
        SteamInventoryItemDto item) =>
        item.AssetProperties.Count == 0
            ? []
            : [.. item.AssetProperties.Select(p => new InventoryAssetProperty(
                PropertyId: p.PropertyId,
                Name: p.Name,
                IntValue: p.IntValue,
                FloatValue: p.FloatValue,
                StringValue: p.StringValue))];

    private static bool BypassCache(InventoryReadFreshness freshness) =>
        freshness == InventoryReadFreshness.Fresh;
}
