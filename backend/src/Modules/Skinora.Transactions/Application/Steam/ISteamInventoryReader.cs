namespace Skinora.Transactions.Application.Steam;

/// <summary>
/// Read port over a seller's Steam inventory used during transaction creation
/// (T45 — 02 §9, 03 §2.2 step 5–8). The real sidecar-backed implementation is
/// delivered in T67 (08 §2.4); until then <see cref="StubSteamInventoryReader"/>
/// keeps the API contract live and is replaced via DI swap without touching
/// callers.
/// </summary>
public interface ISteamInventoryReader
{
    /// <summary>
    /// Resolve a single inventory item by its Steam asset ID for the given
    /// owner. Returns <c>null</c> when the asset is missing or has changed
    /// hands (Steam rotates asset IDs on every trade — 06 §8.4).
    /// </summary>
    Task<InventoryItemSnapshot?> TryGetItemAsync(
        string steamId64,
        string itemAssetId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Item snapshot pulled from the seller's Steam inventory. Mirrors the
/// 06 §3.5 columns the platform persists (<c>ItemAssetId</c>,
/// <c>ItemClassId</c>, <c>ItemName</c>, etc.) plus the tradeability flag
/// enforced before <c>POST /transactions</c> succeeds (03 §2.2 step 8).
/// </summary>
public sealed record InventoryItemSnapshot(
    string AssetId,
    string ClassId,
    string? InstanceId,
    string Name,
    string? IconUrl,
    string? Exterior,
    string? Type,
    string? InspectLink,
    bool IsTradeable);
