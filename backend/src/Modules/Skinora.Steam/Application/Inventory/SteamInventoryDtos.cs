namespace Skinora.Steam.Application.Inventory;

/// <summary>
/// One Steam inventory item — backend-facing shape served by S1
/// (<c>GET /steam/inventory</c>, 07 §6.1) and consumed internally by the T45
/// transaction-creation tradeability check.
/// </summary>
/// <remarks>
/// Field naming mirrors the 07 §6.1 JSON contract verbatim so the controller
/// layer needs no extra projection. The richer fields (<c>ClassId</c>,
/// <c>InstanceId</c>, <c>InspectLink</c>) used by 06 §3.5 columns are
/// optional — present when the sidecar returns them, null otherwise.
/// </remarks>
public sealed record SteamInventoryItemDto(
    string AssetId,
    string ClassId,
    string? InstanceId,
    string Name,
    string MarketHashName,
    string? Type,
    string? Wear,
    string? ImageUrl,
    bool Tradeable,
    bool Marketable)
{
    /// <summary>
    /// T125 — Steam's per-asset <c>asset_properties</c> (T122 runbook §5), when
    /// the sidecar returned them. Empty is ordinary: T122 measured them on 91
    /// of 199 assets (weapons carry them, collectibles do not).
    /// </summary>
    /// <remarks>
    /// Not part of the 07 §6.1 public contract — this is internal audit
    /// material for the delivery launch gate (DEPLOY_RUNBOOK §H), so it is an
    /// init-only member rather than a positional field.
    /// </remarks>
    public IReadOnlyList<SteamInventoryAssetPropertyDto> AssetProperties { get; init; } = [];
}

/// <summary>
/// One <c>asset_properties</c> entry as the sidecar forwards it (T122 runbook
/// §5): <c>Pattern Template</c>, <c>Wear Rating</c>, <c>Item Certificate</c>,
/// <c>Name Tag</c>, <c>Charm Template</c>. Steam sends exactly one of the three
/// value shapes per entry.
/// </summary>
public sealed record SteamInventoryAssetPropertyDto(
    int PropertyId,
    string Name,
    string? IntValue,
    string? FloatValue,
    string? StringValue);

/// <summary>
/// 07 §6.1 response envelope — items plus pre-computed totals to spare every
/// caller a second pass over the list.
/// </summary>
public sealed record SteamInventoryDto(
    IReadOnlyList<SteamInventoryItemDto> Items,
    int TotalCount,
    int TradeableCount);
