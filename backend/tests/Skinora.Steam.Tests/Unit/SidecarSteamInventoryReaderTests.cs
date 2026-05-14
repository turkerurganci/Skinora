using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Steam.Application.Inventory;
using Skinora.Transactions.Application.Steam;

namespace Skinora.Steam.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SidecarSteamInventoryReader"/>. The sidecar HTTP
/// layer is replaced by a fake so the assertions cover the (found / missing
/// / private / unavailable) branches of
/// <see cref="ISteamInventoryReader.TryGetItemAsync"/>.
/// </summary>
public sealed class SidecarSteamInventoryReaderTests
{
    private const string SteamId = "76561198000000111";
    private const string AssetId = "27348562891";

    [Fact]
    public async Task TryGetItemAsync_Returns_Snapshot_When_Asset_Found()
    {
        var fake = new FakeSteamSidecarInventoryClient
        {
            Result = new SteamSidecarInventoryResult(
                SteamSidecarStatus.Success,
                new SteamInventoryDto(
                    Items: new[]
                    {
                        new SteamInventoryItemDto(
                            AssetId, "310776959", "188530139",
                            "AK-47 | Redline",
                            "AK-47 | Redline (Field-Tested)",
                            "Rifle", "Field-Tested",
                            "https://cdn.test/ak.png",
                            Tradeable: true, Marketable: true),
                    },
                    TotalCount: 1,
                    TradeableCount: 1)),
        };
        var sut = new SidecarSteamInventoryReader(
            fake, NullLogger<SidecarSteamInventoryReader>.Instance);

        var snapshot = await sut.TryGetItemAsync(SteamId, AssetId, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(AssetId, snapshot!.AssetId);
        Assert.Equal("310776959", snapshot.ClassId);
        Assert.Equal("188530139", snapshot.InstanceId);
        Assert.Equal("AK-47 | Redline", snapshot.Name);
        Assert.Equal("Field-Tested", snapshot.Exterior);
        Assert.True(snapshot.IsTradeable);
    }

    [Fact]
    public async Task TryGetItemAsync_Returns_Null_When_Asset_Not_In_Inventory()
    {
        var fake = new FakeSteamSidecarInventoryClient
        {
            Result = new SteamSidecarInventoryResult(
                SteamSidecarStatus.Success,
                new SteamInventoryDto(
                    Items: new[]
                    {
                        new SteamInventoryItemDto(
                            "11111111111", "1", "1",
                            "Other", "Other (BS)", null, null, null, true, true),
                    },
                    TotalCount: 1,
                    TradeableCount: 1)),
        };
        var sut = new SidecarSteamInventoryReader(
            fake, NullLogger<SidecarSteamInventoryReader>.Instance);

        var snapshot = await sut.TryGetItemAsync(SteamId, AssetId, CancellationToken.None);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task TryGetItemAsync_Returns_Null_When_Profile_Private()
    {
        var fake = new FakeSteamSidecarInventoryClient
        {
            Result = new SteamSidecarInventoryResult(
                SteamSidecarStatus.InventoryPrivate, Inventory: null),
        };
        var sut = new SidecarSteamInventoryReader(
            fake, NullLogger<SidecarSteamInventoryReader>.Instance);

        var snapshot = await sut.TryGetItemAsync(SteamId, AssetId, CancellationToken.None);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task TryGetItemAsync_Returns_Null_When_Sidecar_Unavailable()
    {
        var fake = new FakeSteamSidecarInventoryClient
        {
            Result = new SteamSidecarInventoryResult(
                SteamSidecarStatus.Unavailable, Inventory: null),
        };
        var sut = new SidecarSteamInventoryReader(
            fake, NullLogger<SidecarSteamInventoryReader>.Instance);

        var snapshot = await sut.TryGetItemAsync(SteamId, AssetId, CancellationToken.None);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task TryGetItemAsync_Returns_Null_On_Empty_Inputs()
    {
        var fake = new FakeSteamSidecarInventoryClient
        {
            Result = new SteamSidecarInventoryResult(
                SteamSidecarStatus.Success,
                new SteamInventoryDto(Array.Empty<SteamInventoryItemDto>(), 0, 0)),
        };
        var sut = new SidecarSteamInventoryReader(
            fake, NullLogger<SidecarSteamInventoryReader>.Instance);

        Assert.Null(await sut.TryGetItemAsync("", AssetId, CancellationToken.None));
        Assert.Null(await sut.TryGetItemAsync(SteamId, "", CancellationToken.None));
        Assert.Equal(0, fake.GetInventoryCalls);
    }

    private sealed class FakeSteamSidecarInventoryClient : ISteamSidecarInventoryClient
    {
        public SteamSidecarInventoryResult Result { get; set; } =
            new(SteamSidecarStatus.Unavailable, Inventory: null);

        public int GetInventoryCalls { get; private set; }

        public Task<SteamSidecarInventoryResult> GetInventoryAsync(
            string steamId, CancellationToken cancellationToken)
        {
            GetInventoryCalls++;
            return Task.FromResult(Result);
        }

        public Task<SteamSidecarStatus> InvalidateInventoryAsync(
            string steamId, CancellationToken cancellationToken)
            => Task.FromResult(SteamSidecarStatus.Success);
    }
}
