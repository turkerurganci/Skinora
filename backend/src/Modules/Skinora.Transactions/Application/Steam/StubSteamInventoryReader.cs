namespace Skinora.Transactions.Application.Steam;

/// <summary>
/// Forward-deferred stub for <see cref="ISteamInventoryReader"/>. Returns
/// <c>null</c> for every lookup so that production callers fail closed
/// (<c>STEAM_INVENTORY_UNAVAILABLE</c>) until the T67 sidecar implementation
/// is wired in. Tests inject their own <c>ISteamInventoryReader</c> double
/// to assert the happy path.
/// </summary>
public sealed class StubSteamInventoryReader : ISteamInventoryReader
{
    public Task<InventoryItemSnapshot?> TryGetItemAsync(
        string steamId64,
        string itemAssetId,
        CancellationToken cancellationToken)
        => Task.FromResult<InventoryItemSnapshot?>(null);
}
