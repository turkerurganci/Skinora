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
        InventoryReadFreshness freshness,
        CancellationToken cancellationToken);

    /// <summary>
    /// T123 — capture the 06 §3.5 delivery baseline: how many copies of one
    /// item class the owner already holds, and which asset IDs those are.
    /// Called on entry to <c>SELLER_CONFIRMED</c> (03 §2.3 step 3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Class-scoped, not inventory-wide, and counted rather than tested for
    /// presence: 02 §9.2 pins delivery evidence to a <em>count</em> delta
    /// because one class legitimately appears many times in the same inventory
    /// (T122 measured 199 assets over 159 distinct classes, the busiest class
    /// holding 9 copies). A presence check would never see a delivery into an
    /// inventory that already contains that skin.
    /// </para>
    /// <para>
    /// Matching is on <c>(classId, instanceId)</c> — the same pair 06 §3.5
    /// names — and never on asset ID, which Steam rotates on every trade.
    /// </para>
    /// </remarks>
    Task<InventoryClassBaselineResult> CaptureClassBaselineAsync(
        string steamId64,
        string classId,
        string? instanceId,
        InventoryReadFreshness freshness,
        CancellationToken cancellationToken);

    /// <summary>
    /// T130 — read the owner's whole inventory as (asset, class, name) triples,
    /// the shape the 03 §6.3 wrong-item comparison needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the class baseline cannot answer this.</b>
    /// <see cref="CaptureClassBaselineAsync"/> is scoped to the transaction's own
    /// item class, so an arrival of a <em>different</em> class is invisible to
    /// it — and a wrong item is, by definition, a different class. Every asset id
    /// the platform records today (<c>Transaction.DeliveredBuyerAssetId</c>) came
    /// out of that class-scoped diff, which is why comparing its class against
    /// <c>Transaction.ItemClassId</c> could only ever match (T130 finding).
    /// Naming what actually arrived needs an inventory-wide reference point.
    /// </para>
    /// <para>
    /// <b>Cost.</b> None beyond the call that is already made: the sidecar
    /// returns the full inventory on every request and both methods above filter
    /// it client-side. This one keeps more of the same response.
    /// </para>
    /// </remarks>
    Task<InventoryFingerprintResult> CaptureInventoryFingerprintAsync(
        string steamId64,
        InventoryReadFreshness freshness,
        CancellationToken cancellationToken);
}

/// <summary>
/// 08 §2.3 — whether a read may be served from the sidecar's 120-second cache.
/// </summary>
/// <remarks>
/// Explicit at the port rather than defaulted, so every call site states which
/// guarantee it needs. The distinction is not a performance knob:
/// <see cref="Fresh"/> is a correctness requirement wherever the read decides
/// whether a transaction may advance (03 §2.3, 07 §7.6a), because a
/// two-minute-old inventory can still show an item the seller has already
/// traded away, and a two-minute-old baseline silently absorbs items the buyer
/// acquired in that window — which would later read as a delivery.
/// </remarks>
public enum InventoryReadFreshness
{
    /// <summary>
    /// The sidecar cache may answer (08 §2.3). Correct for browse/listing reads
    /// and for after-the-fact evidence gathering, where a slightly stale
    /// snapshot costs nothing.
    /// </summary>
    Cached,

    /// <summary>
    /// Bypass the cache read and go to Steam (<c>?refresh=true</c>). Required
    /// wherever the answer gates a state transition or is persisted as a
    /// reference snapshot.
    /// </summary>
    Fresh,
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
/// Outcome of one <see cref="ISteamInventoryReader.CaptureClassBaselineAsync"/>
/// call — the 06 §3.5 <c>BuyerBaseline*</c> snapshot.
/// </summary>
/// <remarks>
/// Same three-valued discipline as <see cref="InventoryLookupResult"/>: a
/// <see cref="InventoryVisibility.Private"/> or
/// <see cref="InventoryVisibility.Unavailable"/> read can never be built
/// carrying a count, so "unreadable" cannot be persisted as a baseline of
/// zero. A zero baseline is a claim ("the buyer holds none of this skin") and
/// a delivery would later be measured against it; an unreadable inventory
/// supports no such claim and must leave the columns NULL (02 §9.2).
/// </remarks>
public sealed record InventoryClassBaselineResult
{
    private IReadOnlyList<string>? _assetIds;

    private InventoryClassBaselineResult(
        InventoryVisibility visibility,
        int classCount,
        IReadOnlyList<InventoryClassAsset> assets,
        IReadOnlyList<string> inventoryClassIds)
    {
        Visibility = visibility;
        ClassCount = classCount;
        Assets = assets;
        InventoryClassIds = inventoryClassIds;
    }

    /// <summary>Which of the 08 §2.3 states the read ended in.</summary>
    public InventoryVisibility Visibility { get; }

    /// <summary>
    /// How many copies of the requested <c>(classId, instanceId)</c> the owner
    /// holds. Meaningful only for <see cref="Captured"/>; zero otherwise.
    /// </summary>
    public int ClassCount { get; }

    /// <summary>
    /// The assets behind <see cref="ClassCount"/>, in the order the sidecar
    /// returned them. Empty for the two unreadable outcomes.
    /// </summary>
    public IReadOnlyList<InventoryClassAsset> Assets { get; }

    /// <summary>
    /// Just the asset IDs of <see cref="Assets"/> — the shape the 06 §3.5
    /// <c>BuyerBaselineAssetIds</c> column persists.
    /// </summary>
    public IReadOnlyList<string> AssetIds =>
        _assetIds ??= [.. Assets.Select(a => a.AssetId)];

    /// <summary>
    /// T130 — every DISTINCT class id in the owner's inventory at capture time,
    /// not just the requested one: the 06 §3.5 <c>BuyerBaselineClassIds</c>
    /// column. Empty for the two unreadable outcomes.
    /// </summary>
    /// <remarks>
    /// Carried on this result rather than fetched separately because the sidecar
    /// hands back the whole inventory anyway — a second call would cost a real
    /// Steam round trip (the baseline is read <see cref="InventoryReadFreshness.Fresh"/>,
    /// so the 120-second cache would not absorb it) to re-read bytes this one
    /// already had.
    /// </remarks>
    public IReadOnlyList<string> InventoryClassIds { get; }

    /// <summary>
    /// Inventory read. A count of zero is a legitimate, useful baseline: the
    /// buyer owns no copy of this skin yet.
    /// </summary>
    public static InventoryClassBaselineResult Captured(
        IReadOnlyList<InventoryClassAsset> assets,
        IReadOnlyList<string> inventoryClassIds)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(inventoryClassIds);
        return new InventoryClassBaselineResult(
            InventoryVisibility.Public, assets.Count, assets, inventoryClassIds);
    }

    /// <summary>Inventory hidden — no baseline can be taken (02 §9.2).</summary>
    public static InventoryClassBaselineResult Private { get; } =
        new(InventoryVisibility.Private, classCount: 0, assets: [], inventoryClassIds: []);

    /// <summary>Steam unreachable — no baseline can be taken.</summary>
    public static InventoryClassBaselineResult Unavailable { get; } =
        new(InventoryVisibility.Unavailable, classCount: 0, assets: [], inventoryClassIds: []);
}

/// <summary>
/// One asset of the requested item class, with the per-asset properties Steam
/// returns alongside it.
/// </summary>
/// <remarks>
/// T125 — the properties are carried for the launch-gate evidence capture
/// (DEPLOY_RUNBOOK §H), never for the delivery decision itself. 02 §9.2 pins
/// that decision to a class <em>count</em> delta; anything per-asset here is
/// audit material a human reads, not an input the evidence engine branches on.
/// </remarks>
public sealed record InventoryClassAsset(
    string AssetId,
    IReadOnlyList<InventoryAssetProperty> Properties)
{
    /// <summary>An asset whose properties Steam did not return (common — T122 measured them on 91 of 199 assets).</summary>
    public static InventoryClassAsset Bare(string assetId) => new(assetId, []);
}

/// <summary>
/// Outcome of one <see cref="ISteamInventoryReader.CaptureInventoryFingerprintAsync"/>
/// call — the inventory-wide reference the 03 §6.3 wrong-item comparison runs
/// against (T130).
/// </summary>
/// <remarks>
/// Same three-valued discipline as the two results above: an unreadable
/// inventory can never be built carrying assets, so "hidden" cannot be diffed
/// as "everything left" (08 §2.3).
/// </remarks>
public sealed record InventoryFingerprintResult
{
    private IReadOnlyList<string>? _classIds;

    private InventoryFingerprintResult(
        InventoryVisibility visibility,
        IReadOnlyList<InventoryFingerprintEntry> assets)
    {
        Visibility = visibility;
        Assets = assets;
    }

    /// <summary>Which of the 08 §2.3 states the read ended in.</summary>
    public InventoryVisibility Visibility { get; }

    /// <summary>
    /// Every asset in the inventory, in the order the sidecar returned them.
    /// Empty for the two unreadable outcomes.
    /// </summary>
    public IReadOnlyList<InventoryFingerprintEntry> Assets { get; }

    /// <summary>
    /// The distinct class ids behind <see cref="Assets"/> — the shape the
    /// 06 §3.5 <c>BuyerBaselineClassIds</c> column persists, and the shape the
    /// wrong-item diff compares against.
    /// </summary>
    public IReadOnlyList<string> ClassIds =>
        _classIds ??= [.. Assets.Select(a => a.ClassId).Distinct(StringComparer.Ordinal)];

    /// <summary>Inventory read. An empty inventory is a legitimate fingerprint.</summary>
    public static InventoryFingerprintResult Captured(IReadOnlyList<InventoryFingerprintEntry> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        return new InventoryFingerprintResult(InventoryVisibility.Public, assets);
    }

    /// <summary>Inventory hidden — nothing is known about what it holds.</summary>
    public static InventoryFingerprintResult Private { get; } =
        new(InventoryVisibility.Private, assets: []);

    /// <summary>Steam unreachable — nothing is known about what it holds.</summary>
    public static InventoryFingerprintResult Unavailable { get; } =
        new(InventoryVisibility.Unavailable, assets: []);
}

/// <summary>
/// One asset of an inventory fingerprint: enough to tell WHICH item it is and
/// to name it for an admin (02 §10.1 third row), and nothing more.
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="InventoryItemSnapshot"/>. A fingerprint
/// spans a third party's whole inventory, and its only job is the class diff
/// plus a human-readable name for the one item that diff singles out — so it
/// carries no icons, no inspect links and no per-asset properties (runbook §8:
/// third-party inventory contents are personal data).
/// </remarks>
public sealed record InventoryFingerprintEntry(
    string AssetId,
    string ClassId,
    string? InstanceId,
    string Name)
{
    /// <summary>
    /// Whether this asset is the <c>(classId, instanceId)</c> pair 06 §3.5 names
    /// — the same match rule the class baseline uses, so a count taken from a
    /// fingerprint is comparable with <c>BuyerBaselineClassCount</c>.
    /// </summary>
    public bool Matches(string classId, string? instanceId)
    {
        if (!string.Equals(ClassId, classId, StringComparison.Ordinal)) return false;
        return instanceId is null
            || string.Equals(InstanceId, instanceId, StringComparison.Ordinal);
    }
}

/// <summary>
/// One entry of Steam's per-asset <c>asset_properties</c> array (T122 runbook
/// §5): <c>Pattern Template</c>, <c>Wear Rating</c>, <c>Item Certificate</c>,
/// <c>Name Tag</c>, <c>Charm Template</c>.
/// </summary>
/// <remarks>
/// <para>
/// Steam types the value three ways and sends exactly one of them per entry,
/// so all three are nullable rather than collapsed into a single string — the
/// distinction is what a later reviewer needs to tell a wear float from a
/// certificate hash.
/// </para>
/// <para>
/// Why the platform carries these at all: T122 could not measure whether an
/// <c>Item Certificate</c> survives a trade (runbook §7, B3), and that question
/// can only be answered from real deliveries. The launch gate collects the
/// material; it does <em>not</em> license anyone to branch on it (02 §9.2).
/// </para>
/// </remarks>
public sealed record InventoryAssetProperty(
    int PropertyId,
    string Name,
    string? IntValue,
    string? FloatValue,
    string? StringValue);

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
    bool IsTradeable)
{
    /// <summary>
    /// T125 — Steam's per-asset <c>asset_properties</c> (T122 runbook §5).
    /// Empty when Steam returned none for this asset, which is ordinary.
    /// </summary>
    /// <remarks>
    /// Deliberately an init-only member rather than an eleventh positional
    /// parameter: this is audit material for the launch-gate capture
    /// (DEPLOY_RUNBOOK §H), not a field any decision reads, so forcing every
    /// existing construction site to state it would misrepresent its weight.
    /// The money-safety fields above stay positional exactly because a caller
    /// <em>must</em> be made to think about them.
    /// </remarks>
    public IReadOnlyList<InventoryAssetProperty> AssetProperties { get; init; } = [];
}
