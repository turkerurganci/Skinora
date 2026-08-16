using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Settlement;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Transactions.Tests.Integration.Lifecycle;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Settlement;

/// <summary>
/// T129 — the 02 §4.5.1 end-of-window check.
///
/// The cases below are the four ways this question can be answered and the two
/// ways it is easy to answer wrongly. Getting it wrong is symmetric in cost:
/// a false "verified" pays a seller who reversed the trade, a false "reversed"
/// refunds a buyer who merely sold the skin on and fraud-flags an honest
/// seller. Each therefore gets its own named test.
/// </summary>
public class SettlementVerificationServiceTests : IntegrationTestBase
{
    static SettlementVerificationServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string SellerSteamId = "76561198000000190";
    private const string BuyerSteamId = "76561198000000191";
    private const string ItemAssetId = "27348562891";
    private const string DeliveredAssetId = "99887766";
    private const string ItemClassId = "310776959";
    private const string ItemInstanceId = "188530139";
    private const string ValidWallet1 = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";
    private const string ValidWallet2 = "TabcDEFGHJKLMNPQRSTUVWXYZ234567Xyz";

    private User _seller = null!;
    private User _buyer = null!;
    private FakeTimeProvider _clock = null!;
    private FakeSteamInventoryReader _inventory = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteamId,
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = ValidWallet1,
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = BuyerSteamId,
            SteamDisplayName = "Buyer",
        };
        context.Set<User>().AddRange(_seller, _buyer);
        await context.SaveChangesAsync();

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        _inventory = new FakeSteamInventoryReader();
    }

    // ================= Verified =================

    [Fact]
    public async Task DeliveredAssetStillWithBuyer_IsVerified()
    {
        var transaction = await CreateDeliveredAsync();
        RegisterBuyerAsset(DeliveredAssetId);

        var result = await Verify(transaction);

        Assert.Equal(SettlementVerdict.Verified, result.Verdict);
        Assert.True(result.BuyerHoldsItem);
        // The seller's side is not read when the item is where it should be —
        // there is nothing to disambiguate and reads are rate-limited.
        Assert.Null(result.SellerVisibility);
        Assert.Single(_inventory.ItemReadFreshness);
        Assert.Equal(InventoryReadFreshness.Fresh, _inventory.ItemReadFreshness[0]);
    }

    [Fact]
    public async Task BuyerBoughtAnotherCopy_OfTheSameSkin_IsStillVerified()
    {
        // The count route must not read "exactly baseline + 1" — an eight-day
        // window is long enough for the buyer to acquire more copies of the same
        // skin, and treating that as a reversal would refund a settled trade.
        var transaction = await CreateDeliveredAsync(deliveredBuyerAssetId: null, baselineClassCount: 1);
        RegisterBuyerAsset(DeliveredAssetId);
        RegisterBuyerAsset("11112222");
        RegisterBuyerAsset("33334444");

        var result = await Verify(transaction);

        Assert.Equal(SettlementVerdict.Verified, result.Verdict);
        Assert.Equal(3, result.ObservedClassCount);
        Assert.Equal(2, result.ExpectedClassCount);
    }

    // ================= Reversal =================

    [Fact]
    public async Task ItemGoneFromBuyer_AndBackWithSeller_IsReversalSignature()
    {
        var transaction = await CreateDeliveredAsync();
        // Nothing registered for the buyer: the delivered asset is gone.
        RegisterSellerAsset(ItemAssetId);

        var result = await Verify(transaction);

        Assert.Equal(SettlementVerdict.ReversalSignature, result.Verdict);
        Assert.False(result.BuyerHoldsItem);
        Assert.True(result.SellerAssetReturned);
    }

    [Fact]
    public async Task CountRoute_DropsBackToBaseline_AndSellerHasItBack_IsReversalSignature()
    {
        var transaction = await CreateDeliveredAsync(deliveredBuyerAssetId: null, baselineClassCount: 2);
        // Buyer is back down to their two original copies.
        RegisterBuyerAsset("11112222");
        RegisterBuyerAsset("33334444");
        RegisterSellerAsset(ItemAssetId);

        var result = await Verify(transaction);

        Assert.Equal(SettlementVerdict.ReversalSignature, result.Verdict);
        Assert.Equal(2, result.ObservedClassCount);
        Assert.Equal(3, result.ExpectedClassCount);
    }

    // ================= Ambiguous departure =================

    [Fact]
    public async Task ItemGoneFromBuyer_ButNotBackWithSeller_IsAmbiguous()
    {
        // The buyer traded the skin onward — legitimate once Steam's 7-day
        // restriction expires, which happens a day before the default window
        // closes. Refunding here would hand the buyer the mirror image of the
        // fraud this whole mechanism exists to stop.
        var transaction = await CreateDeliveredAsync();

        var result = await Verify(transaction);

        Assert.Equal(SettlementVerdict.AmbiguousDeparture, result.Verdict);
        Assert.False(result.BuyerHoldsItem);
        Assert.False(result.SellerAssetReturned);
    }

    // ================= Inconclusive =================

    [Fact]
    public async Task BuyerInventoryPrivate_IsInconclusive_NotADeparture()
    {
        // A hidden inventory says nothing about where the item is. Reading it as
        // "gone" would put an honest transaction on the reversal path.
        var transaction = await CreateDeliveredAsync();
        _inventory.ForcedVisibility = InventoryVisibility.Private;

        var result = await Verify(transaction);

        Assert.Equal(SettlementVerdict.Inconclusive, result.Verdict);
        Assert.Null(result.BuyerHoldsItem);
        Assert.Equal(InventoryVisibility.Private, result.BuyerVisibility);
    }

    [Fact]
    public async Task BuyerInventoryUnreadable_IsInconclusive_NotAReversal()
    {
        var transaction = await CreateDeliveredAsync();
        _inventory.ForcedVisibility = InventoryVisibility.Unavailable;

        var result = await Verify(transaction);

        Assert.Equal(SettlementVerdict.Inconclusive, result.Verdict);
        Assert.Null(result.BuyerHoldsItem);
        Assert.Equal(InventoryVisibility.Unavailable, result.BuyerVisibility);
    }

    [Fact]
    public async Task NoDeliveredAssetId_AndNoBaseline_IsInconclusive()
    {
        // Buyer-confirmed delivery with a private buyer inventory at
        // SELLER_CONFIRMED: no asset id was recorded and no count exists to
        // measure against. 06 §3.5 — a NULL baseline is not a zero baseline.
        var transaction = await CreateDeliveredAsync(
            deliveredBuyerAssetId: null, baselineClassCount: null);

        var result = await Verify(transaction);

        Assert.Equal(SettlementVerdict.Inconclusive, result.Verdict);
        Assert.Null(result.BuyerHoldsItem);
        // No inventory read is spent on a question with no reference.
        Assert.Empty(_inventory.BaselineReadFreshness);
    }

    // ================= Helpers =================

    private Task<SettlementVerificationResult> Verify(Transaction transaction) =>
        new SettlementVerificationService(
                Context, _inventory, NullLogger<SettlementVerificationService>.Instance)
            .VerifyAsync(transaction, CancellationToken.None);

    private void RegisterBuyerAsset(string assetId) =>
        _inventory.Register(BuyerSteamId, ItemSnapshot(assetId));

    private void RegisterSellerAsset(string assetId) =>
        _inventory.Register(SellerSteamId, ItemSnapshot(assetId));

    private static InventoryItemSnapshot ItemSnapshot(string assetId) =>
        new(
            AssetId: assetId,
            ClassId: ItemClassId,
            InstanceId: ItemInstanceId,
            Name: "AK-47 | Redline",
            MarketHashName: "AK-47 | Redline (Field-Tested)",
            IconUrl: null,
            Exterior: "Field-Tested",
            Type: "Rifle",
            InspectLink: null,
            IsTradeable: true);

    private async Task<Transaction> CreateDeliveredAsync(
        string? deliveredBuyerAssetId = DeliveredAssetId,
        int? baselineClassCount = 0)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var deliveredAt = nowUtc.AddDays(-8);
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.ITEM_DELIVERED,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = BuyerSteamId,
            BuyerRefundAddress = ValidWallet2,
            BuyerTradeUrl = "https://steamcommunity.com/tradeoffer/new/?partner=1&token=abc",
            ItemAssetId = ItemAssetId,
            ItemClassId = ItemClassId,
            ItemInstanceId = ItemInstanceId,
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = ValidWallet1,
            PaymentTimeoutMinutes = 1440,
            AcceptedAt = deliveredAt.AddHours(-3),
            SellerReadyConfirmedAt = deliveredAt.AddHours(-2),
            PaymentReceivedAt = deliveredAt.AddHours(-1),
            ItemDeliveredAt = deliveredAt,
            DeliveryVerifiedAt = deliveredAt,
            DeliveryEvidence = DeliveryEvidence.BUYER_CONFIRMED,
            DeliveredBuyerAssetId = deliveredBuyerAssetId,
            PayoutEligibleAt = deliveredAt.AddDays(8),
            BuyerBaselineClassCount = baselineClassCount,
            BuyerBaselineAssetIds = baselineClassCount is null ? null : JsonSerializer.Serialize(Array.Empty<string>()),
            BuyerBaselineCapturedAt = baselineClassCount is null ? null : deliveredAt.AddHours(-2),
        };
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return transaction;
    }
}
