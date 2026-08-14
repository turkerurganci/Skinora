using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Delivery;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Transactions.Tests.Integration.Lifecycle;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Delivery;

/// <summary>
/// T125 — the 02 §9.2 delivery evidence engine.
///
/// The first region walks the §9.2 trap matrix row by row. Each row is a way of
/// getting delivery verification wrong that costs somebody real money, so each
/// gets its own test rather than being folded into a table-driven case: the
/// failure message has to name the trap.
/// </summary>
public class DeliveryVerificationServiceTests : IntegrationTestBase
{
    static DeliveryVerificationServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string SellerSteamId = "76561198000000090";
    private const string BuyerSteamId = "76561198000000091";
    private const string ItemAssetId = "27348562891";
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

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        _inventory = new FakeSteamInventoryReader();
    }

    // ================= 02 §9.2 trap matrix =================

    /// <summary>Row 1 — buyer confirmation is sufficient on its own.</summary>
    [Fact]
    public async Task Row01_BuyerConfirmed_Alone_Is_Delivered()
    {
        // No inventory is registered at all: the seller's item is "missing" and
        // the buyer holds nothing. If the engine consulted Steam here it would
        // find a misdelivery signature and argue with the buyer.
        var transaction = await CreateTransactionAsync(
            evidence: DeliveryEvidence.BUYER_CONFIRMED);

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.Delivered, result.Verdict);
        Assert.False(result.AutoReleaseGated);
        // The confirmation is against the buyer's own interest, so no Steam read
        // can improve on it — and none is spent (02 §9.2).
        Assert.Empty(_inventory.ItemReadFreshness);
        Assert.Empty(_inventory.BaselineReadFreshness);
    }

    /// <summary>Row 2 — the conjunction: asset gone AND the buyer's count rose.</summary>
    [Fact]
    public async Task Row02_AssetGone_And_InventoryDelta_Is_Delivered()
    {
        await OpenLaunchGateAsync();
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        // Seller no longer holds it; buyer now holds one copy of the class.
        RegisterBuyerCopies("99887766");

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.Delivered, result.Verdict);
        Assert.Equal(
            DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA,
            result.Evidence);
    }

    /// <summary>
    /// Row 3 — the buyer's count rose but the seller still holds the item. The
    /// buyer acquired the same skin somewhere else; paying out here would send
    /// the money to a seller who never delivered.
    /// </summary>
    [Fact]
    public async Task Row03_InventoryDelta_Without_AssetGone_Is_Not_Delivered()
    {
        await OpenLaunchGateAsync();
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        RegisterSellerStillHoldsItem();
        RegisterBuyerCopies("55554444");

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.NoMovement, result.Verdict);
        Assert.Equal(DeliveryEvidence.INVENTORY_DELTA, result.Evidence);
        Assert.False(result.Evidence.IsSufficientForDelivery());
    }

    /// <summary>
    /// Row 4 — the item left the seller and never arrived: wrong item, or a send
    /// to a third party. Escalates; never resolves silently (02 §10.1).
    /// </summary>
    [Fact]
    public async Task Row04_AssetGone_Without_Delta_Is_Misdelivery_Signature()
    {
        await OpenLaunchGateAsync();
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        // Seller's asset absent, buyer's class count unchanged at 0.

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.MisdeliverySignature, result.Verdict);
        Assert.True(result.Evidence.IsMisdeliverySignature());
        Assert.False(result.Evidence.IsSufficientForDelivery());
    }

    /// <summary>Row 5 — nothing moved on either side: the seller has not sent yet.</summary>
    [Fact]
    public async Task Row05_No_Movement_On_Either_Side()
    {
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        RegisterSellerStillHoldsItem();

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.NoMovement, result.Verdict);
        Assert.Equal(DeliveryEvidence.NONE, result.Evidence);
    }

    /// <summary>
    /// Row 6 — the buyer's inventory is unreadable. The inventory path is
    /// CLOSED, not negative: "private" must never be read as "nothing arrived".
    /// </summary>
    [Theory]
    [InlineData(InventoryVisibility.Private)]
    [InlineData(InventoryVisibility.Unavailable)]
    public async Task Row06_Unreadable_Buyer_Inventory_Is_Inconclusive(InventoryVisibility visibility)
    {
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        RegisterSellerStillHoldsItem();
        _inventory.ForcedBaselineVisibility = visibility;

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.Inconclusive, result.Verdict);
        Assert.Equal(visibility, result.BuyerVisibility);
        Assert.Null(result.ObservedClassCount);
        Assert.False(result.Evidence.HasFlag(DeliveryEvidence.INVENTORY_DELTA));
    }

    /// <summary>
    /// Row 6b — the same unreadable buyer, but the seller's asset IS gone. This
    /// is the expensive trap: the recorded flags now satisfy
    /// <c>IsMisdeliverySignature()</c>, yet the platform never looked at the
    /// buyer. Calling it misdelivery would accuse a seller who may well have
    /// delivered.
    /// </summary>
    [Theory]
    [InlineData(InventoryVisibility.Private)]
    [InlineData(InventoryVisibility.Unavailable)]
    public async Task Row06b_AssetGone_With_Unreadable_Buyer_Is_Not_Misdelivery(
        InventoryVisibility visibility)
    {
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        _inventory.ForcedBaselineVisibility = visibility;

        var result = await Verify(transaction);

        Assert.True(result.Evidence.IsMisdeliverySignature());
        Assert.Equal(DeliveryVerdict.Inconclusive, result.Verdict);
        Assert.NotEqual(DeliveryVerdict.MisdeliverySignature, result.Verdict);
    }

    /// <summary>
    /// Row 7 — the seller's inventory is unreadable. "Could not read" is not
    /// "the asset is gone" (08 §2.3), so no SELLER_ASSET_GONE flag is raised.
    /// </summary>
    [Theory]
    [InlineData(InventoryVisibility.Private)]
    [InlineData(InventoryVisibility.Unavailable)]
    public async Task Row07_Unreadable_Seller_Inventory_Is_Inconclusive(InventoryVisibility visibility)
    {
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        _inventory.ForcedVisibility = visibility;

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.Inconclusive, result.Verdict);
        Assert.Equal(visibility, result.SellerVisibility);
        Assert.False(result.Evidence.HasFlag(DeliveryEvidence.SELLER_ASSET_GONE));
    }

    /// <summary>
    /// Row 8 — no baseline was ever captured (the buyer's inventory was hidden
    /// at SELLER_CONFIRMED). There is nothing for a count to be measured
    /// against, so the buyer's inventory is not even read.
    /// </summary>
    [Fact]
    public async Task Row08_Missing_Baseline_Closes_The_Inventory_Path()
    {
        var transaction = await CreateTransactionAsync(baselineClassCount: null);
        RegisterSellerStillHoldsItem();
        RegisterBuyerCopies("11112222", "33334444");

        var result = await Verify(transaction);

        Assert.False(result.BaselineAvailable);
        Assert.Equal(DeliveryVerdict.Inconclusive, result.Verdict);
        Assert.Null(result.BuyerVisibility);
        // No baseline ⇒ no read: a count with nothing to compare it against
        // would only tempt a later reader to compare it with zero.
        Assert.Empty(_inventory.BaselineReadFreshness);
        Assert.False(result.Evidence.HasFlag(DeliveryEvidence.INVENTORY_DELTA));
    }

    /// <summary>
    /// Row 9 — the buyer already owned copies of this skin. 02 §9.2 is a COUNTING
    /// rule: a presence check would never see this delivery.
    /// </summary>
    [Fact]
    public async Task Row09_Delta_Is_Counted_Against_Preexisting_Copies()
    {
        await OpenLaunchGateAsync();
        // Baseline: the buyer already held two copies of the class.
        var transaction = await CreateTransactionAsync(
            baselineClassCount: 2,
            baselineAssetIds: ["10001", "10002"]);
        RegisterBuyerCopies("10001", "10002", "99999");

        var result = await Verify(transaction);

        Assert.Equal(3, result.ObservedClassCount);
        Assert.Equal(2, result.BaselineClassCount);
        Assert.Equal(DeliveryVerdict.Delivered, result.Verdict);
        Assert.True(result.Evidence.HasFlag(DeliveryEvidence.INVENTORY_DELTA));
    }

    /// <summary>
    /// Row 10 — the count did not rise. Equal is not a delivery, and neither is
    /// a decrease (the buyer may have traded one away).
    /// </summary>
    [Theory]
    [InlineData(2, new[] { "10001", "10002" })]
    [InlineData(3, new[] { "10001", "10002" })]
    public async Task Row10_Unchanged_Or_Lower_Count_Is_No_Delta(
        int baselineCount, string[] currentAssets)
    {
        var transaction = await CreateTransactionAsync(baselineClassCount: baselineCount);
        RegisterSellerStillHoldsItem();
        RegisterBuyerCopies(currentAssets);

        var result = await Verify(transaction);

        Assert.False(result.Evidence.HasFlag(DeliveryEvidence.INVENTORY_DELTA));
        Assert.Equal(DeliveryVerdict.NoMovement, result.Verdict);
    }

    /// <summary>
    /// Row 11 — the buyer side is matched by CLASS, never by asset ID. Steam
    /// rotates the ID on every trade (06 §8.4), so the delivered copy arrives
    /// under an ID the platform has never seen.
    /// </summary>
    [Fact]
    public async Task Row11_Buyer_Match_Ignores_AssetId_Rotation()
    {
        await OpenLaunchGateAsync();
        var transaction = await CreateTransactionAsync(
            baselineClassCount: 0, baselineAssetIds: []);
        // A brand-new asset ID, unrelated to Transaction.ItemAssetId.
        RegisterBuyerCopies("70000000001");

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.Delivered, result.Verdict);
        // 06 §8.4 best-effort audit field: the arrival is named by diffing the
        // baseline ID list, not by looking for ItemAssetId.
        Assert.Equal("70000000001", result.CandidateDeliveredAssetId);
        Assert.NotEqual(ItemAssetId, result.CandidateDeliveredAssetId);
    }

    /// <summary>
    /// Row 12 — lock state is not an evidence input. T122-B8 measured
    /// <c>market_tradable_restriction: 7</c> on a freely tradable item, and the
    /// anonymous <c>tradable</c> flag is class-level with no expiry (runbook
    /// §6), so a cooldown has no signature the platform can observe.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Row12_Verdict_Is_Independent_Of_Item_Lock_State(bool tradeable)
    {
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        RegisterSellerStillHoldsItem(tradeable);

        var result = await Verify(transaction);

        // Same verdict either way. An untradeable item in the seller's
        // inventory is still IN the seller's inventory — the only question
        // §9.2 asks of that side.
        Assert.Equal(DeliveryVerdict.NoMovement, result.Verdict);
        Assert.False(result.Evidence.HasFlag(DeliveryEvidence.SELLER_ASSET_GONE));
    }

    /// <summary>
    /// Row 13 — wear / pattern differences are out of automatic scope (02 §9.2,
    /// T130 handles them as WRONG_ITEM). The properties ARE carried, but only
    /// into the audit capture: a same-class item with a completely different
    /// wear float still verifies as delivered.
    /// </summary>
    [Fact]
    public async Task Row13_Wear_And_Pattern_Differences_Do_Not_Change_The_Verdict()
    {
        await OpenLaunchGateAsync();
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        _inventory.Register(BuyerSteamId, ItemSnapshot("70000000002", tradeable: true) with
        {
            AssetProperties =
            [
                new InventoryAssetProperty(2, "Wear Rating", null, "0.9999", null),
                new InventoryAssetProperty(1, "Pattern Template", "3", null, null),
            ],
        });

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.Delivered, result.Verdict);
        // Recorded for the human reviewer (DEPLOY_RUNBOOK §H), not branched on.
        Assert.NotNull(result.Capture);
        Assert.Contains(
            result.Capture!.ObservedAssets.SelectMany(a => a.Properties),
            p => p.Name == "Wear Rating" && p.FloatValue == "0.9999");
    }

    // ================= Launch gate (AC6 — DEPLOY_RUNBOOK §H) =================

    [Fact]
    public async Task Gate_Closed_Holds_Inventory_Evidence_For_Review()
    {
        // Seed default is false; no ConfigureSetting call here on purpose.
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        RegisterBuyerCopies("99887766");

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.InventoryEvidencePendingReview, result.Verdict);
        Assert.True(result.AutoReleaseGated);
        // The evidence is real and is preserved — the gate withholds the money
        // movement, it does not deny the finding or turn it into a cancellation.
        Assert.True(result.Evidence.IsSufficientForDelivery());
    }

    [Fact]
    public async Task Gate_Defaults_To_Closed_When_The_Setting_Is_Missing()
    {
        await Context.Set<SystemSetting>()
            .Where(s => s.Key == DeliveryVerificationService.AutoReleaseSettingKey)
            .ExecuteDeleteAsync();
        Context.ChangeTracker.Clear();

        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        RegisterBuyerCopies("99887766");

        var result = await Verify(transaction);

        // Fail-closed: an absent switch delays a payout until a human looks,
        // which is the cheap direction (T122 runbook §7).
        Assert.True(result.AutoReleaseGated);
    }

    [Fact]
    public async Task Gate_Does_Not_Apply_To_Buyer_Confirmation()
    {
        // Gate closed. The buyer's own "I received it" is their decision, not
        // the platform's inference, so it releases regardless.
        var transaction = await CreateTransactionAsync(
            baselineClassCount: 0, evidence: DeliveryEvidence.BUYER_CONFIRMED);

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.Delivered, result.Verdict);
        Assert.False(result.AutoReleaseGated);
    }

    [Fact]
    public async Task Gate_Open_Releases_On_Inventory_Evidence()
    {
        await OpenLaunchGateAsync();
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        RegisterBuyerCopies("99887766");

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.Delivered, result.Verdict);
        Assert.False(result.AutoReleaseGated);
    }

    [Fact]
    public async Task Gate_Does_Not_Suppress_The_Misdelivery_Escalation()
    {
        // Gate closed. A misdelivery signature moves no money — it moves the
        // case to an admin — so the gate must not swallow it.
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.MisdeliverySignature, result.Verdict);
        Assert.False(result.AutoReleaseGated);
    }

    // ================= Capture (AC6 storage) =================

    [Fact]
    public async Task Capture_Records_Both_Sides_And_The_Latency_Timestamps()
    {
        var transaction = await CreateTransactionAsync(
            baselineClassCount: 0,
            baselineAssetIds: [],
            paymentReceivedAt: _clock.GetUtcNow().UtcDateTime.AddMinutes(-42));
        _inventory.Register(BuyerSteamId, ItemSnapshot("70000000003", tradeable: true) with
        {
            AssetProperties =
            [
                new InventoryAssetProperty(6, "Item Certificate", null, null, "B0A0654AAF3FF0"),
            ],
        });

        var result = await Verify(transaction);
        var capture = result.Capture;

        Assert.NotNull(capture);
        // B1 — delivery latency is derivable: payment moment vs observation.
        Assert.Equal(transaction.PaymentReceivedAt, capture!.PaymentReceivedAt);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, capture.ObservedAt);
        // B2 — asset-ID rotation: what the seller listed vs what arrived.
        Assert.Equal(ItemAssetId, capture.SellerItemAssetId);
        Assert.Equal(new[] { "70000000003" }, capture.NewAssetIds);
        // B3 — Item Certificate persistence across the trade.
        Assert.Contains(
            capture.ObservedAssets.SelectMany(a => a.Properties),
            p => p.Name == "Item Certificate" && p.StringValue == "B0A0654AAF3FF0");
        Assert.Equal("Public", capture.SellerVisibility);
        Assert.Equal("Public", capture.BuyerVisibility);
    }

    [Fact]
    public async Task Capture_Is_Skipped_For_Rounds_A_Reviewer_Would_Not_Read()
    {
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        RegisterSellerStillHoldsItem();

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.NoMovement, result.Verdict);
        // A poll that found nothing is not evidence about anything; capturing
        // every one would bury the rows that matter.
        Assert.Null(result.Capture);
    }

    [Fact]
    public async Task Recorder_Persists_The_Capture_As_An_Append_Only_Row()
    {
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        RegisterBuyerCopies("99887766");
        var result = await Verify(transaction);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        var row = DeliveryEvidenceCaptureRecorder.Record(Context, transaction, result, nowUtc);
        await Context.SaveChangesAsync();

        Assert.NotNull(row);
        var persisted = await Context.Set<DeliveryEvidenceCapture>().AsNoTracking()
            .SingleAsync(c => c.TransactionId == transaction.Id);
        Assert.Equal(nameof(DeliveryVerdict.InventoryEvidencePendingReview), persisted.Verdict);
        Assert.True(persisted.AutoReleaseGated);
        Assert.Equal(
            DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA,
            persisted.Evidence);

        // The payload is the reviewer's actual material — it has to round-trip.
        var payload = JsonSerializer.Deserialize<DeliveryEvidenceCaptureData>(persisted.Payload);
        Assert.NotNull(payload);
        Assert.Equal(ItemClassId, payload!.ItemClassId);
        Assert.Equal(new[] { "99887766" }, payload.NewAssetIds);
    }

    [Fact]
    public async Task Recorder_Rejects_An_Update_To_A_Written_Capture()
    {
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        RegisterBuyerCopies("99887766");
        var result = await Verify(transaction);
        DeliveryEvidenceCaptureRecorder.Record(
            Context, transaction, result, _clock.GetUtcNow().UtcDateTime);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var row = await Context.Set<DeliveryEvidenceCapture>()
            .SingleAsync(c => c.TransactionId == transaction.Id);
        row.Verdict = nameof(DeliveryVerdict.Delivered);

        // 06 §4.2 — the rows justify a decision about someone's money. One that
        // could be edited afterwards is worth nothing to the reviewer.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Recorder_Writes_Nothing_When_There_Is_No_Capture()
    {
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        RegisterSellerStillHoldsItem();
        var result = await Verify(transaction);

        var row = DeliveryEvidenceCaptureRecorder.Record(
            Context, transaction, result, _clock.GetUtcNow().UtcDateTime);
        await Context.SaveChangesAsync();

        Assert.Null(row);
        Assert.Empty(await Context.Set<DeliveryEvidenceCapture>().AsNoTracking().ToListAsync());
    }

    // ================= Purity / polling safety (AC2) =================

    [Fact]
    public async Task Verification_Writes_Nothing_And_Repeats_Identically()
    {
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        RegisterBuyerCopies("99887766");

        var first = await Verify(transaction);
        var second = await Verify(transaction);

        Assert.Equal(first.Verdict, second.Verdict);
        Assert.Equal(first.Evidence, second.Evidence);

        // The transaction is untouched — in memory and on disk. A caller that
        // polls this engine must be able to do so without changing the answer.
        Assert.Equal(DeliveryEvidence.NONE, transaction.DeliveryEvidence);
        Assert.Null(transaction.DeliveryVerifiedAt);
        Context.ChangeTracker.Clear();
        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(DeliveryEvidence.NONE, persisted.DeliveryEvidence);
        Assert.Null(persisted.DeliveredBuyerAssetId);
        Assert.Empty(await Context.Set<DeliveryEvidenceCapture>().AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Callers_Choose_The_Read_Freshness()
    {
        var transaction = await CreateTransactionAsync(baselineClassCount: 0);
        RegisterSellerStillHoldsItem();

        await Verify(transaction, InventoryReadFreshness.Fresh);
        await Verify(transaction, InventoryReadFreshness.Cached);

        // 02 §10.1 requires the dispute path to re-run the rules "taze olarak",
        // so the choice belongs to the caller rather than being baked in here.
        Assert.Equal(
            new[] { InventoryReadFreshness.Fresh, InventoryReadFreshness.Cached },
            _inventory.ItemReadFreshness);
        Assert.Equal(
            new[] { InventoryReadFreshness.Fresh, InventoryReadFreshness.Cached },
            _inventory.BaselineReadFreshness);
    }

    // ================= Recorded-evidence merge =================

    [Fact]
    public async Task Evidence_Recorded_By_An_Earlier_Round_Completes_The_Conjunction()
    {
        await OpenLaunchGateAsync();
        // An earlier round saw the buyer's count rise; this one sees the
        // seller's asset leave. Both halves are monotonic facts about the same
        // delivery, so together they satisfy 02 §9.2.
        var transaction = await CreateTransactionAsync(
            baselineClassCount: 0, evidence: DeliveryEvidence.INVENTORY_DELTA);
        _inventory.ForcedBaselineVisibility = InventoryVisibility.Private;

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.Delivered, result.Verdict);
        Assert.Equal(DeliveryEvidence.SELLER_ASSET_GONE, result.ObservedEvidence);
    }

    // ================= DeliveredBuyerAssetId (06 §8.4 best-effort) =========

    [Fact]
    public async Task Ambiguous_Arrival_Names_No_Asset()
    {
        await OpenLaunchGateAsync();
        var transaction = await CreateTransactionAsync(
            baselineClassCount: 0, baselineAssetIds: []);
        // Two new copies appeared; nothing says which one this transaction paid
        // for. Naming the wrong one would mislead WRONG_ITEM handling (02 §10.1).
        RegisterBuyerCopies("80000000001", "80000000002");

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.Delivered, result.Verdict);
        Assert.Null(result.CandidateDeliveredAssetId);
    }

    [Fact]
    public async Task Truncated_Baseline_Suppresses_The_Asset_Diff_But_Not_The_Verdict()
    {
        await OpenLaunchGateAsync();
        // T123 caps BuyerBaselineAssetIds at 400 characters; the COUNT stays
        // exact. Every ID the truncation dropped would otherwise look new.
        var transaction = await CreateTransactionAsync(
            baselineClassCount: 3, baselineAssetIds: ["10001"]);
        RegisterBuyerCopies("10001", "10002", "10003", "10004");

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.Delivered, result.Verdict);
        Assert.Null(result.CandidateDeliveredAssetId);
        Assert.True(result.Capture!.BaselineAssetIdsTruncated);
        Assert.Empty(result.Capture.NewAssetIds);
    }

    [Fact]
    public async Task Unparsable_Baseline_Id_List_Degrades_To_No_Candidate()
    {
        await OpenLaunchGateAsync();
        // The buyer held one copy at baseline, but the ID list is corrupt. The
        // count is the authority (02 §9.2 is a counting rule), so the delivery
        // still verifies — but the corrupt list cannot be diffed, and the
        // pre-existing copy would otherwise be reported as a fresh arrival.
        var transaction = await CreateTransactionAsync(baselineClassCount: 1);
        transaction.BuyerBaselineAssetIds = "not-json";
        Context.Set<Transaction>().Update(transaction);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        RegisterBuyerCopies("10001", "90000000001");

        var result = await Verify(transaction);

        Assert.Equal(DeliveryVerdict.Delivered, result.Verdict);
        Assert.Null(result.CandidateDeliveredAssetId);
        Assert.Empty(result.Capture!.NewAssetIds);
    }

    [Fact]
    public async Task Empty_Baseline_Still_Names_The_Arrival()
    {
        await OpenLaunchGateAsync();
        // The counterpart of the case above: a baseline of zero copies has an
        // empty ID list legitimately, so every asset of that class present now
        // IS new and may be named (06 §8.4).
        var transaction = await CreateTransactionAsync(
            baselineClassCount: 0, baselineAssetIds: []);
        RegisterBuyerCopies("90000000001");

        var result = await Verify(transaction);

        Assert.Equal("90000000001", result.CandidateDeliveredAssetId);
        Assert.False(result.Capture!.BaselineAssetIdsTruncated);
    }

    // ================= Helpers =================

    private DeliveryVerificationService BuildSut() =>
        new(Context, _inventory, NullLogger<DeliveryVerificationService>.Instance, _clock);

    private Task<DeliveryVerificationResult> Verify(
        Transaction transaction,
        InventoryReadFreshness freshness = InventoryReadFreshness.Fresh)
        => BuildSut().VerifyAsync(transaction, freshness, CancellationToken.None);

    private Task OpenLaunchGateAsync() =>
        Context.ConfigureSettingAsync(DeliveryVerificationService.AutoReleaseSettingKey, "true");

    /// <summary>The seller's listed asset is still in their inventory.</summary>
    private void RegisterSellerStillHoldsItem(bool tradeable = true) =>
        _inventory.Register(SellerSteamId, ItemSnapshot(ItemAssetId, tradeable));

    /// <summary>Register the buyer's current copies of the transaction's item class.</summary>
    private void RegisterBuyerCopies(params string[] assetIds)
    {
        foreach (var assetId in assetIds)
            _inventory.Register(BuyerSteamId, ItemSnapshot(assetId, tradeable: true));
    }

    private static InventoryItemSnapshot ItemSnapshot(string assetId, bool tradeable) =>
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
            IsTradeable: tradeable);

    private async Task<Transaction> CreateTransactionAsync(
        int? baselineClassCount = 0,
        IReadOnlyList<string>? baselineAssetIds = null,
        DeliveryEvidence evidence = DeliveryEvidence.NONE,
        DateTime? paymentReceivedAt = null)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.PAYMENT_RECEIVED,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = BuyerSteamId,
            BuyerRefundAddress = ValidWallet2,
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
            AcceptedAt = nowUtc.AddHours(-2),
            SellerReadyConfirmedAt = nowUtc.AddHours(-1),
            PaymentReceivedAt = paymentReceivedAt ?? nowUtc.AddMinutes(-30),
            DeliveryDeadline = nowUtc.AddHours(23),
            DeliveryEvidence = evidence,
            // A baseline exists only when it was actually captured — 06 §3.5
            // keeps all three columns NULL together when it was not.
            BuyerBaselineClassCount = baselineClassCount,
            BuyerBaselineAssetIds = baselineClassCount is null
                ? null
                : JsonSerializer.Serialize(baselineAssetIds ?? []),
            BuyerBaselineCapturedAt = baselineClassCount is null ? null : nowUtc.AddHours(-1),
        };
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return transaction;
    }
}
