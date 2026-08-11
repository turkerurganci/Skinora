using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Steam.Application.Inventory;
using Skinora.Transactions.Application.Steam;

namespace Skinora.Steam.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SidecarSteamInventoryReader"/>. The sidecar HTTP
/// layer is replaced by a fake so the assertions cover the (found / absent /
/// private / unavailable) branches of
/// <see cref="ISteamInventoryReader.GetItemAsync"/>.
/// </summary>
/// <remarks>
/// T121 — before this task all four branches returned <c>null</c>, so no test
/// could tell them apart. The assertions below are the AC's "observable at the
/// port level": each branch pins a distinct
/// <see cref="InventoryLookupResult.Visibility"/>.
/// </remarks>
public sealed class SidecarSteamInventoryReaderTests
{
    private const string SteamId = "76561198000000111";
    private const string AssetId = "27348562891";

    [Fact]
    public async Task GetItemAsync_Returns_Public_With_Snapshot_When_Asset_Found()
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

        var result = await sut.GetItemAsync(SteamId, AssetId, CancellationToken.None);

        Assert.Equal(InventoryVisibility.Public, result.Visibility);
        Assert.NotNull(result.Item);
        Assert.Equal(AssetId, result.Item!.AssetId);
        Assert.Equal("310776959", result.Item.ClassId);
        Assert.Equal("188530139", result.Item.InstanceId);
        Assert.Equal("AK-47 | Redline", result.Item.Name);
        Assert.Equal("AK-47 | Redline (Field-Tested)", result.Item.MarketHashName);
        Assert.Equal("Field-Tested", result.Item.Exterior);
        Assert.True(result.Item.IsTradeable);
    }

    [Fact]
    public async Task GetItemAsync_Returns_Public_Without_Item_When_Asset_Not_In_Inventory()
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

        var result = await sut.GetItemAsync(SteamId, AssetId, CancellationToken.None);

        // The inventory WAS read — this is evidence of absence, not absence of
        // evidence (08 §2.3). It is the only branch a caller may act on as
        // "the item is not there".
        Assert.Equal(InventoryVisibility.Public, result.Visibility);
        Assert.Null(result.Item);
    }

    [Fact]
    public async Task GetItemAsync_Returns_Public_Without_Item_When_Inventory_Empty()
    {
        var fake = new FakeSteamSidecarInventoryClient
        {
            Result = new SteamSidecarInventoryResult(
                SteamSidecarStatus.Success,
                new SteamInventoryDto(Array.Empty<SteamInventoryItemDto>(), 0, 0)),
        };
        var sut = new SidecarSteamInventoryReader(
            fake, NullLogger<SidecarSteamInventoryReader>.Instance);

        var result = await sut.GetItemAsync(SteamId, AssetId, CancellationToken.None);

        Assert.Equal(InventoryVisibility.Public, result.Visibility);
        Assert.Null(result.Item);
    }

    [Fact]
    public async Task GetItemAsync_Returns_Private_When_Profile_Private()
    {
        var fake = new FakeSteamSidecarInventoryClient
        {
            Result = new SteamSidecarInventoryResult(
                SteamSidecarStatus.InventoryPrivate, Inventory: null),
        };
        var sut = new SidecarSteamInventoryReader(
            fake, NullLogger<SidecarSteamInventoryReader>.Instance);

        var result = await sut.GetItemAsync(SteamId, AssetId, CancellationToken.None);

        Assert.Equal(InventoryVisibility.Private, result.Visibility);
        Assert.Null(result.Item);
    }

    [Fact]
    public async Task GetItemAsync_Returns_Unavailable_When_Sidecar_Unavailable()
    {
        var fake = new FakeSteamSidecarInventoryClient
        {
            Result = new SteamSidecarInventoryResult(
                SteamSidecarStatus.Unavailable, Inventory: null),
        };
        var sut = new SidecarSteamInventoryReader(
            fake, NullLogger<SidecarSteamInventoryReader>.Instance);

        var result = await sut.GetItemAsync(SteamId, AssetId, CancellationToken.None);

        Assert.Equal(InventoryVisibility.Unavailable, result.Visibility);
        Assert.Null(result.Item);
    }

    [Fact]
    public async Task GetItemAsync_Distinguishes_Private_From_Unavailable_From_Empty()
    {
        // The core of T121: the three "no item" answers must not compare equal.
        // Before this task every one of them was `null`.
        var empty = await ReadWith(new SteamSidecarInventoryResult(
            SteamSidecarStatus.Success,
            new SteamInventoryDto(Array.Empty<SteamInventoryItemDto>(), 0, 0)));
        var priv = await ReadWith(new SteamSidecarInventoryResult(
            SteamSidecarStatus.InventoryPrivate, Inventory: null));
        var down = await ReadWith(new SteamSidecarInventoryResult(
            SteamSidecarStatus.Unavailable, Inventory: null));

        Assert.NotEqual(empty.Visibility, priv.Visibility);
        Assert.NotEqual(priv.Visibility, down.Visibility);
        Assert.NotEqual(empty.Visibility, down.Visibility);
        Assert.All(new[] { empty, priv, down }, r => Assert.Null(r.Item));
    }

    [Fact]
    public async Task GetItemAsync_Returns_Unavailable_On_Empty_Inputs_Without_Calling_Sidecar()
    {
        var fake = new FakeSteamSidecarInventoryClient
        {
            Result = new SteamSidecarInventoryResult(
                SteamSidecarStatus.Success,
                new SteamInventoryDto(Array.Empty<SteamInventoryItemDto>(), 0, 0)),
        };
        var sut = new SidecarSteamInventoryReader(
            fake, NullLogger<SidecarSteamInventoryReader>.Instance);

        // A blank identifier means no read happened, so it must not be reported
        // as a readable-but-empty inventory: that would be manufactured
        // evidence that the asset is gone.
        var blankSteamId = await sut.GetItemAsync("", AssetId, CancellationToken.None);
        var blankAssetId = await sut.GetItemAsync(SteamId, "", CancellationToken.None);

        Assert.Equal(InventoryVisibility.Unavailable, blankSteamId.Visibility);
        Assert.Equal(InventoryVisibility.Unavailable, blankAssetId.Visibility);
        Assert.Equal(0, fake.GetInventoryCalls);
    }

    [Fact]
    public async Task GetItemAsync_Returns_Unavailable_When_Success_Carries_No_Envelope()
    {
        // Contract violation from the client layer: Success must always carry an
        // envelope. Falling back to "readable and empty" would invent evidence.
        var result = await ReadWith(new SteamSidecarInventoryResult(
            SteamSidecarStatus.Success, Inventory: null));

        Assert.Equal(InventoryVisibility.Unavailable, result.Visibility);
        Assert.Null(result.Item);
    }

    private static async Task<InventoryLookupResult> ReadWith(SteamSidecarInventoryResult sidecarResult)
    {
        var sut = new SidecarSteamInventoryReader(
            new FakeSteamSidecarInventoryClient { Result = sidecarResult },
            NullLogger<SidecarSteamInventoryReader>.Instance);

        return await sut.GetItemAsync(SteamId, AssetId, CancellationToken.None);
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
