using Skinora.Steam.Application.Inventory;

namespace Skinora.Steam.Tests.Unit;

/// <summary>
/// The S1 listing surface (07 §6.1) maps every sidecar status onto its own
/// controller status. The endpoint tests drive the controller through a fake
/// query service, so this mapping is pinned here, against the real service.
/// </summary>
public sealed class SteamInventoryQueryServiceTests
{
    [Theory]
    [InlineData(SteamSidecarStatus.InventoryPrivate, GetInventoryStatus.InventoryPrivate)]
    [InlineData(SteamSidecarStatus.Unavailable, GetInventoryStatus.SteamUnavailable)]
    // P2P-InventoryUnauthorizedMapping — Steam's 401 keeps its own status all
    // the way to the controller, which answers 422 INVENTORY_NOT_FOUND; falling
    // into the default arm would turn it back into a retryable 503.
    [InlineData(SteamSidecarStatus.InventoryNotFound, GetInventoryStatus.InventoryNotFound)]
    public async Task Maps_Each_Unreadable_Sidecar_Status_To_Its_Own_Listing_Status(
        SteamSidecarStatus sidecarStatus, GetInventoryStatus expected)
    {
        var sut = new SteamInventoryQueryService(
            new FixedSidecar(new SteamSidecarInventoryResult(sidecarStatus, Inventory: null)));

        var result = await sut.GetForSteamIdAsync("76561198000000001", CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Inventory);
    }

    [Fact]
    public async Task Returns_The_Inventory_On_Success()
    {
        var inventory = new SteamInventoryDto(Array.Empty<SteamInventoryItemDto>(), 0, 0);
        var sut = new SteamInventoryQueryService(
            new FixedSidecar(new SteamSidecarInventoryResult(SteamSidecarStatus.Success, inventory)));

        var result = await sut.GetForSteamIdAsync("76561198000000001", CancellationToken.None);

        Assert.Equal(GetInventoryStatus.Success, result.Status);
        Assert.Same(inventory, result.Inventory);
    }

    private sealed class FixedSidecar(SteamSidecarInventoryResult result) : ISteamSidecarInventoryClient
    {
        public Task<SteamSidecarInventoryResult> GetInventoryAsync(
            string steamId, bool bypassCache, CancellationToken cancellationToken)
            => Task.FromResult(result);

        public Task<SteamSidecarStatus> InvalidateInventoryAsync(
            string steamId, CancellationToken cancellationToken)
            => Task.FromResult(SteamSidecarStatus.Success);
    }
}
