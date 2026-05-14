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
    bool Marketable);

/// <summary>
/// 07 §6.1 response envelope — items plus pre-computed totals to spare every
/// caller a second pass over the list.
/// </summary>
public sealed record SteamInventoryDto(
    IReadOnlyList<SteamInventoryItemDto> Items,
    int TotalCount,
    int TradeableCount);
