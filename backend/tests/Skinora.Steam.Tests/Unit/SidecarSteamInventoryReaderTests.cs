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

        var result = await sut.GetItemAsync(SteamId, AssetId, InventoryReadFreshness.Cached, CancellationToken.None);

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

        var result = await sut.GetItemAsync(SteamId, AssetId, InventoryReadFreshness.Cached, CancellationToken.None);

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

        var result = await sut.GetItemAsync(SteamId, AssetId, InventoryReadFreshness.Cached, CancellationToken.None);

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

        var result = await sut.GetItemAsync(SteamId, AssetId, InventoryReadFreshness.Cached, CancellationToken.None);

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

        var result = await sut.GetItemAsync(SteamId, AssetId, InventoryReadFreshness.Cached, CancellationToken.None);

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
        var blankSteamId = await sut.GetItemAsync("", AssetId, InventoryReadFreshness.Cached, CancellationToken.None);
        var blankAssetId = await sut.GetItemAsync(SteamId, "", InventoryReadFreshness.Cached, CancellationToken.None);

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

        return await sut.GetItemAsync(
            SteamId, AssetId, InventoryReadFreshness.Cached, CancellationToken.None);
    }

    // ---------- T123: freshness + delivery baseline ----------

    [Theory]
    [InlineData(InventoryReadFreshness.Cached, false)]
    [InlineData(InventoryReadFreshness.Fresh, true)]
    public async Task Freshness_Is_Threaded_Down_To_The_Sidecar_Cache_Flag(
        InventoryReadFreshness freshness, bool expectedBypass)
    {
        var fake = new FakeSteamSidecarInventoryClient
        {
            Result = new SteamSidecarInventoryResult(
                SteamSidecarStatus.Success,
                new SteamInventoryDto(Array.Empty<SteamInventoryItemDto>(), 0, 0)),
        };
        var sut = new SidecarSteamInventoryReader(
            fake, NullLogger<SidecarSteamInventoryReader>.Instance);

        await sut.GetItemAsync(SteamId, AssetId, freshness, CancellationToken.None);
        await sut.CaptureClassBaselineAsync(
            SteamId, "310776959", "188530139", freshness, CancellationToken.None);

        Assert.Equal([expectedBypass, expectedBypass], fake.BypassCacheCalls);
    }

    [Fact]
    public async Task CaptureClassBaseline_Counts_Every_Copy_Of_The_Class_Not_Just_One()
    {
        // 02 §9.2 is a COUNTING rule. T122 measured a real inventory with 9
        // copies of a single class; a presence check would never see a delivery
        // into it, which is exactly why the baseline stores a count.
        var fake = new FakeSteamSidecarInventoryClient
        {
            Result = new SteamSidecarInventoryResult(
                SteamSidecarStatus.Success,
                new SteamInventoryDto(
                    Items:
                    [
                        Item("111", "310776959", "188530139"),
                        Item("222", "310776959", "188530139"),
                        Item("333", "999999999", "188530139"),  // different class
                        Item("444", "310776959", "777777777"),  // different instance
                    ],
                    TotalCount: 4,
                    TradeableCount: 4)),
        };
        var sut = new SidecarSteamInventoryReader(
            fake, NullLogger<SidecarSteamInventoryReader>.Instance);

        var result = await sut.CaptureClassBaselineAsync(
            SteamId, "310776959", "188530139",
            InventoryReadFreshness.Fresh, CancellationToken.None);

        Assert.Equal(InventoryVisibility.Public, result.Visibility);
        Assert.Equal(2, result.ClassCount);
        Assert.Equal(["111", "222"], result.AssetIds);
    }

    [Fact]
    public async Task CaptureClassBaseline_Matches_On_Class_Alone_When_InstanceId_Is_Null()
    {
        // A listing created without an instance id must not silently produce an
        // empty baseline — an empty baseline is a claim, not a gap.
        var fake = new FakeSteamSidecarInventoryClient
        {
            Result = new SteamSidecarInventoryResult(
                SteamSidecarStatus.Success,
                new SteamInventoryDto(
                    Items: [Item("111", "310776959", "188530139")],
                    TotalCount: 1,
                    TradeableCount: 1)),
        };
        var sut = new SidecarSteamInventoryReader(
            fake, NullLogger<SidecarSteamInventoryReader>.Instance);

        var result = await sut.CaptureClassBaselineAsync(
            SteamId, "310776959", instanceId: null,
            InventoryReadFreshness.Fresh, CancellationToken.None);

        Assert.Equal(1, result.ClassCount);
    }

    [Fact]
    public async Task CaptureClassBaseline_Distinguishes_Empty_From_Private_From_Unavailable()
    {
        // The T121 discipline applied to the baseline: "the buyer owns none of
        // this skin" is evidence and may be persisted; the other two are
        // ignorance and must leave the 06 §3.5 columns NULL.
        var empty = await BaselineWith(new SteamSidecarInventoryResult(
            SteamSidecarStatus.Success,
            new SteamInventoryDto(Array.Empty<SteamInventoryItemDto>(), 0, 0)));
        var priv = await BaselineWith(new SteamSidecarInventoryResult(
            SteamSidecarStatus.InventoryPrivate, Inventory: null));
        var down = await BaselineWith(new SteamSidecarInventoryResult(
            SteamSidecarStatus.Unavailable, Inventory: null));

        Assert.Equal(InventoryVisibility.Public, empty.Visibility);
        Assert.Equal(0, empty.ClassCount);
        Assert.Equal(InventoryVisibility.Private, priv.Visibility);
        Assert.Equal(InventoryVisibility.Unavailable, down.Visibility);
        Assert.All(new[] { priv, down }, r => Assert.Empty(r.AssetIds));
    }

    [Fact]
    public async Task CaptureClassBaseline_Returns_Unavailable_On_Blank_Inputs_Without_Calling_Sidecar()
    {
        var fake = new FakeSteamSidecarInventoryClient
        {
            Result = new SteamSidecarInventoryResult(
                SteamSidecarStatus.Success,
                new SteamInventoryDto(Array.Empty<SteamInventoryItemDto>(), 0, 0)),
        };
        var sut = new SidecarSteamInventoryReader(
            fake, NullLogger<SidecarSteamInventoryReader>.Instance);

        var blankSteamId = await sut.CaptureClassBaselineAsync(
            "", "310776959", null, InventoryReadFreshness.Fresh, CancellationToken.None);
        var blankClassId = await sut.CaptureClassBaselineAsync(
            SteamId, "", null, InventoryReadFreshness.Fresh, CancellationToken.None);

        Assert.Equal(InventoryVisibility.Unavailable, blankSteamId.Visibility);
        Assert.Equal(InventoryVisibility.Unavailable, blankClassId.Visibility);
        Assert.Equal(0, fake.GetInventoryCalls);
    }

    private static async Task<InventoryClassBaselineResult> BaselineWith(
        SteamSidecarInventoryResult sidecarResult)
    {
        var sut = new SidecarSteamInventoryReader(
            new FakeSteamSidecarInventoryClient { Result = sidecarResult },
            NullLogger<SidecarSteamInventoryReader>.Instance);

        return await sut.CaptureClassBaselineAsync(
            SteamId, "310776959", "188530139",
            InventoryReadFreshness.Fresh, CancellationToken.None);
    }

    private static SteamInventoryItemDto Item(string assetId, string classId, string? instanceId) =>
        new(assetId, classId, instanceId, "AK-47 | Redline",
            "AK-47 | Redline (Field-Tested)", "Rifle", "Field-Tested",
            "https://cdn.test/ak.png", Tradeable: true, Marketable: true);

    private sealed class FakeSteamSidecarInventoryClient : ISteamSidecarInventoryClient
    {
        public SteamSidecarInventoryResult Result { get; set; } =
            new(SteamSidecarStatus.Unavailable, Inventory: null);

        public int GetInventoryCalls { get; private set; }

        /// <summary>T123 — the <c>bypassCache</c> flag of each call, in order.</summary>
        public List<bool> BypassCacheCalls { get; } = [];

        public Task<SteamSidecarInventoryResult> GetInventoryAsync(
            string steamId, bool bypassCache, CancellationToken cancellationToken)
        {
            GetInventoryCalls++;
            BypassCacheCalls.Add(bypassCache);
            return Task.FromResult(Result);
        }

        public Task<SteamSidecarStatus> InvalidateInventoryAsync(
            string steamId, CancellationToken cancellationToken)
            => Task.FromResult(SteamSidecarStatus.Success);
    }
}
