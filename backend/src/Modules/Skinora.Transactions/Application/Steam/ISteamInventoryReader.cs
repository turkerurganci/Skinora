namespace Skinora.Transactions.Application.Steam;

/// <summary>
/// Read port over a Steam inventory (02 §9, 03 §2.2 step 5–8). The
/// sidecar-backed implementation lives in Skinora.Steam
/// (<c>SidecarSteamInventoryReader</c>); <see cref="StubSteamInventoryReader"/>
/// keeps the contract live where the Steam module is not registered and is
/// replaced via DI swap without touching callers.
/// </summary>
public interface ISteamInventoryReader
{
    /// <summary>
    /// Resolve a single inventory item by its Steam asset ID for the given
    /// owner. Never returns <c>null</c>: the outcome carries the 08 §2.3
    /// three-valued visibility so callers cannot mistake "Steam could not be
    /// read" for "the item is not there" (see
    /// <see cref="InventoryLookupResult"/>).
    /// </summary>
    /// <remarks>
    /// Steam rotates asset IDs on every trade (06 §8.4), so a
    /// <see cref="InventoryVisibility.Public"/> read with no matching asset is
    /// evidence about <em>this</em> asset ID only.
    /// </remarks>
    Task<InventoryLookupResult> GetItemAsync(
        string steamId64,
        string itemAssetId,
        CancellationToken cancellationToken);
}

/// <summary>
/// 08 §2.3 — the three states an inventory read can end in (v3.0).
/// </summary>
/// <remarks>
/// The distinction is money-safety critical: delivery verification (02 §9.2)
/// reads both sides' inventories, and treating an unreadable inventory as
/// "the item never arrived" refunds a transaction that was in fact settled.
/// </remarks>
public enum InventoryVisibility
{
    /// <summary>
    /// The inventory was read. Whatever it says — including "the asset is not
    /// in it" — is evidence.
    /// </summary>
    Public,

    /// <summary>
    /// The profile/inventory is hidden. The evidence path is closed, not
    /// negative: nothing at all is known about the asset. Retrying does not
    /// help; the user has to act (08 §2.7 — not retryable).
    /// </summary>
    Private,

    /// <summary>
    /// Steam (or the sidecar) could not be reached. Absence of information,
    /// never a negative answer. Retryable (08 §2.7).
    /// </summary>
    Unavailable,
}

/// <summary>
/// Outcome of one <see cref="ISteamInventoryReader.GetItemAsync"/> call.
/// </summary>
/// <remarks>
/// <para>
/// The constructor is private on purpose: the four factories below are the
/// only reachable shapes, so a <see cref="InventoryVisibility.Private"/> or
/// <see cref="InventoryVisibility.Unavailable"/> result can never be built
/// carrying an item, and a caller cannot reconstruct the pre-T121 collapse by
/// pairing an arbitrary visibility with <c>null</c>.
/// </para>
/// <para>
/// Callers must switch on <see cref="Visibility"/> first. Testing
/// <c>Item is null</c> alone re-creates exactly the bug this type exists to
/// remove: it reads "Steam is down" as "the seller does not have the item".
/// </para>
/// </remarks>
public sealed record InventoryLookupResult
{
    private InventoryLookupResult(InventoryVisibility visibility, InventoryItemSnapshot? item)
    {
        Visibility = visibility;
        Item = item;
    }

    /// <summary>Which of the 08 §2.3 states the read ended in.</summary>
    public InventoryVisibility Visibility { get; }

    /// <summary>
    /// The resolved item. Non-null only for <see cref="Found"/>; a
    /// <see cref="Public"/> read that did not contain the asset carries
    /// <c>null</c> and <em>is</em> the evidence that it is absent.
    /// </summary>
    public InventoryItemSnapshot? Item { get; }

    /// <summary>Inventory read; the asset is present.</summary>
    public static InventoryLookupResult Found(InventoryItemSnapshot item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new InventoryLookupResult(InventoryVisibility.Public, item);
    }

    /// <summary>Inventory read; the asset is not in it (a positive finding).</summary>
    public static InventoryLookupResult NotFound { get; } =
        new(InventoryVisibility.Public, item: null);

    /// <summary>Inventory hidden — nothing is known about the asset.</summary>
    public static InventoryLookupResult Private { get; } =
        new(InventoryVisibility.Private, item: null);

    /// <summary>Steam unreachable — nothing is known about the asset.</summary>
    public static InventoryLookupResult Unavailable { get; } =
        new(InventoryVisibility.Unavailable, item: null);
}

/// <summary>
/// Item snapshot pulled from a Steam inventory. Mirrors the
/// 06 §3.5 columns the platform persists (<c>ItemAssetId</c>,
/// <c>ItemClassId</c>, <c>ItemName</c>, etc.) plus the tradeability flag
/// enforced before <c>POST /transactions</c> succeeds (03 §2.2 step 8).
/// </summary>
/// <remarks>
/// <para>
/// <c>MarketHashName</c> is the canonical Steam market key (e.g.
/// "AK-47 | Redline (Field-Tested)") — distinct from the display
/// <c>Name</c> ("AK-47 | Redline"), which drops the wear/variant suffix and
/// is locale-dependent. WP4a threads it from the sidecar (which already
/// merges it from the item descriptions) into the fraud pre-check so the
/// PRICE_DEVIATION rule can look up the market price. It is consumed
/// transiently at creation and is <em>not</em> a persisted 06 §3.5 column —
/// the price rule runs only at creation (02 §14.4), never at accept.
/// </para>
/// </remarks>
public sealed record InventoryItemSnapshot(
    string AssetId,
    string ClassId,
    string? InstanceId,
    string Name,
    string MarketHashName,
    string? IconUrl,
    string? Exterior,
    string? Type,
    string? InspectLink,
    bool IsTradeable);
