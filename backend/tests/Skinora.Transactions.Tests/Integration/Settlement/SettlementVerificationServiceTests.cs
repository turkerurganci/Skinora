using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
        // Full verification round at delivery: the platform watched the asset
        // leave the seller, so its reappearance now is a RETURN (02 §4.5.1).
        var transaction = await CreateDeliveredAsync(
            evidence: DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA);
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
        // Inventory-evidence delivery that recorded no single candidate asset id
        // (more than one new asset appeared), so the buyer side falls to the
        // count route while the departure is still on file.
        var transaction = await CreateDeliveredAsync(
            deliveredBuyerAssetId: null,
            baselineClassCount: 2,
            evidence: DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA);
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

    /// <summary>
    /// The mirror-image harm the two-sided check exists to prevent, closed at
    /// the other end (validator finding N1). 02 §4.5.1 asks whether the item
    /// RETURNED to the seller; presence alone does not say that. On a delivery
    /// closed by the buyer's confirmation the platform never reads the seller's
    /// inventory, so an honest seller who sent a different copy of the same skin
    /// — a valid delivery under the §9.2 counting rule — still has the original
    /// asset sitting there. Calling that a reversal would refund the buyer, let
    /// them keep the skin and flag the seller.
    /// </summary>
    [Fact]
    public async Task SellerHasTheAsset_ButItsDepartureWasNeverObserved_IsAmbiguous_NotAReversal()
    {
        var transaction = await CreateDeliveredAsync(); // buyer-confirmed route
        RegisterSellerAsset(ItemAssetId);

        var result = await Verify(transaction);

        Assert.Equal(SettlementVerdict.AmbiguousDeparture, result.Verdict);
        Assert.False(result.BuyerHoldsItem);
        // The read itself is preserved — the admin is told the asset IS there,
        // just that its departure was never on file.
        Assert.True(result.SellerAssetReturned);
        Assert.Contains("AYRILDIĞI hiç gözlenmedi", result.Detail);
    }

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

    // ================= No decision input =================

    /// <summary>
    /// Buyer-confirmed delivery with a private buyer inventory at
    /// SELLER_CONFIRMED: no asset id was recorded and no count exists to measure
    /// against (06 §3.5 — a NULL baseline is not a zero baseline). Its own
    /// verdict rather than <c>Inconclusive</c>, because neither column is
    /// writable after ITEM_DELIVERED: retrying this one is not "wait and see",
    /// it is waiting for something that cannot happen (validator finding B1).
    /// </summary>
    [Fact]
    public async Task NoDeliveredAssetId_AndNoBaseline_IsNoDeliveryReference_NotInconclusive()
    {
        var transaction = await CreateDeliveredAsync(
            deliveredBuyerAssetId: null, baselineClassCount: null);

        var result = await Verify(transaction);

        Assert.Equal(SettlementVerdict.NoDeliveryReference, result.Verdict);
        Assert.Null(result.BuyerHoldsItem);
        // No inventory read is spent on a question with no reference.
        Assert.Empty(_inventory.BaselineReadFreshness);
    }

    /// <summary>
    /// The missing reference is decided BEFORE the buyer's Steam account is
    /// resolved, so a buyer the platform cannot resolve does not turn a
    /// permanently unanswerable case into "could not read". Ordering mattered
    /// once the escalation reasons became sticky: the row would have been
    /// escalated as <c>SETTLEMENT_UNREADABLE</c> after 48 hours of pointless
    /// retries, and that label — pinned by the anti-downgrade rule — sends the
    /// admin to DEPLOY_RUNBOOK §I.3, whose triage explicitly does not apply to
    /// this class (§I.5 does). Owner decision, 2026-08-17.
    /// </summary>
    [Fact]
    public async Task NoDeliveryReference_IsDecided_BeforeTheBuyerAccountIsResolved()
    {
        var transaction = await CreateDeliveredAsync(
            deliveredBuyerAssetId: null, baselineClassCount: null);

        // The buyer can no longer be resolved (soft-deleted → query filter).
        var buyer = await Context.Set<User>().FirstAsync(u => u.Id == _buyer.Id);
        buyer.IsDeleted = true;
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var result = await Verify(transaction);

        Assert.Equal(SettlementVerdict.NoDeliveryReference, result.Verdict);
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

    /// <param name="evidence">
    /// How the delivery was proven. The default is the buyer-confirmation route,
    /// which reads no inventory at all — so it never records that the seller's
    /// asset LEFT, and a settlement round cannot call its reappearance a
    /// reversal. Tests that want the reversal signature have to say the full
    /// verification round ran.
    /// </param>
    private async Task<Transaction> CreateDeliveredAsync(
        string? deliveredBuyerAssetId = DeliveredAssetId,
        int? baselineClassCount = 0,
        DeliveryEvidence evidence = DeliveryEvidence.BUYER_CONFIRMED)
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
            DeliveryEvidence = evidence,
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
