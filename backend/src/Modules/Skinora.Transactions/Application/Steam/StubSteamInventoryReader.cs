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
        CancellationToken cancellationToken)
        => Task.FromResult(InventoryLookupResult.Unavailable);
}
