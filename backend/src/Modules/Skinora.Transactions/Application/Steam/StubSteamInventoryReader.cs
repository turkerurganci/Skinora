namespace Skinora.Transactions.Application.Steam;

/// <summary>
/// Fallback <see cref="ISteamInventoryReader"/> used where the Steam module is
/// not registered. Answers <see cref="InventoryLookupResult.Unavailable"/> for
/// every lookup, which is the literal truth — there is no sidecar to ask — and
/// keeps production callers failing closed on <c>STEAM_UNAVAILABLE</c>.
/// </summary>
/// <remarks>
/// T121 changed the answer from "asset not found" to "inventory unavailable".
/// The old value was a lie with a cost: it told a seller their item was not in
/// their inventory whenever the read path was simply absent (08 §2.3 — an
/// unreadable inventory is never a negative answer). Tests inject their own
/// <see cref="ISteamInventoryReader"/> double to assert the happy path.
/// </remarks>
public sealed class StubSteamInventoryReader : ISteamInventoryReader
{
    public Task<InventoryLookupResult> GetItemAsync(
        string steamId64,
        string itemAssetId,
        InventoryReadFreshness freshness,
        CancellationToken cancellationToken)
        => Task.FromResult(InventoryLookupResult.Unavailable);

    /// <summary>
    /// T123 — same reasoning as <see cref="GetItemAsync"/>: with no sidecar
    /// there is no snapshot, and an empty baseline would be a claim about the
    /// buyer's inventory rather than an admission of ignorance (02 §9.2).
    /// </summary>
    public Task<InventoryClassBaselineResult> CaptureClassBaselineAsync(
        string steamId64,
        string classId,
        string? instanceId,
        InventoryReadFreshness freshness,
        CancellationToken cancellationToken)
        => Task.FromResult(InventoryClassBaselineResult.Unavailable);

    /// <summary>
    /// T130 — same reasoning again. An empty fingerprint would diff as "the
    /// buyer's inventory holds nothing", which with a recorded baseline reads as
    /// every item having left it (08 §2.3).
    /// </summary>
    public Task<InventoryFingerprintResult> CaptureInventoryFingerprintAsync(
        string steamId64,
        InventoryReadFreshness freshness,
        CancellationToken cancellationToken)
        => Task.FromResult(InventoryFingerprintResult.Unavailable);
}
