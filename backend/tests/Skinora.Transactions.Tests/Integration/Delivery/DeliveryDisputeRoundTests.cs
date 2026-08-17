using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Delivery;
using Skinora.Transactions.Application.Settlement;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Transactions.Tests.Integration.Lifecycle;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Delivery;

/// <summary>
/// T130 — <see cref="DeliveryDisputeRound"/>, the fresh 02 §9.2 round a DELIVERY
/// dispute opens with (02 §10.1: "§9.2 kanıt kuralları taze olarak
/// çalıştırılır").
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <c>DeliveryTimeoutRoundTests</c>, and the suite is organised
/// around what makes the two different: a timeout may cancel, a dispute may not.
/// The buyer asked a question — every arm here either advances the transaction
/// or leaves it exactly where it was, and the last test pins that as a rule
/// rather than as a property of the arms that happen to exist today.
/// </para>
/// </remarks>
public class DeliveryDisputeRoundTests : IntegrationTestBase
{
    static DeliveryDisputeRoundTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string SellerSteamId = "76561198000000190";
    private const string BuyerSteamId = "76561198000000191";
    private const string AdminSteamId = "76561198000000192";
    private const string ItemAssetId = "27348562891";
    private const string ItemClassId = "310776959";
    private const string ItemInstanceId = "188530139";
    private const string ValidWallet1 = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";
    private const string ValidWallet2 = "TabcDEFGHJKLMNPQRSTUVWXYZ234567Xyz";
    private const ulong SteamId64ToId32Offset = 76561197960265728UL;

    private static readonly string BuyerTradeUrl =
        "https://steamcommunity.com/tradeoffer/new/"
        + $"?partner={ulong.Parse(BuyerSteamId) - SteamId64ToId32Offset}&token=AbCdEfGh";

    private User _seller = null!;
    private User _buyer = null!;
    private User _admin = null!;
    private FakeTimeProvider _clock = null!;
    private FakeSteamInventoryReader _inventory = null!;
    private RecordingOutboxService _outbox = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteamId,
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = ValidWallet1,
        };
        _buyer = new User { Id = Guid.NewGuid(), SteamId = BuyerSteamId, SteamDisplayName = "Buyer" };
        _admin = new User { Id = Guid.NewGuid(), SteamId = AdminSteamId, SteamDisplayName = "Admin" };
        context.Set<User>().AddRange(_seller, _buyer, _admin);
        await context.SaveChangesAsync();

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
        _inventory = new FakeSteamInventoryReader();
        _outbox = new RecordingOutboxService();
    }

    // ================= Sonuç A — delivery proven =================

    /// <summary>
    /// 03 §6.2 Sonuç A — "İşlem ITEM_DELIVERED durumuna geçer, dispute anında
    /// kapanır". The transition belongs to the round, not to the dispute
    /// service, and it carries the T129 settlement window with it.
    /// </summary>
    [Fact]
    public async Task Delivered_Advances_The_Transaction_And_Opens_The_Settlement_Window()
    {
        await OpenLaunchGateAsync();
        var transaction = await CreateAsync();
        RegisterBuyerCopies("99887766");   // seller's asset unregistered ⇒ gone

        var outcome = await Run(transaction);

        Assert.Equal(DeliveryDisputeOutcome.Delivered, outcome);

        // The round writes onto the caller's unit of work and never saves — the
        // dispute service does, once. Persisting here is therefore also an
        // assertion that the round left the right things tracked.
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persisted.Status);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.DeliveryVerifiedAt);
        Assert.NotNull(persisted.PayoutEligibleAt);
        Assert.Equal(
            DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA,
            persisted.DeliveryEvidence);

        // 06 §3.5 — the buyer opened a dispute, they did not confirm receipt.
        Assert.Null(persisted.BuyerConfirmedReceiptAt);
        // 06 §8.4 — exactly one asset appeared, so it can be named.
        Assert.Equal("99887766", persisted.DeliveredBuyerAssetId);

        var statusEvent = Assert.Single(_outbox.Published.OfType<TransactionStatusChangedEvent>());
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, statusEvent.FromStatus);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, statusEvent.ToStatus);

        // WP15 — SYSTEM actor: the conclusion is the platform's inference.
        var history = await Context.Set<TransactionHistory>().AsNoTracking()
            .Where(h => h.TransactionId == transaction.Id)
            .ToListAsync();
        Assert.Equal(ActorType.SYSTEM, Assert.Single(history).ActorType);
    }

    /// <summary>
    /// 02 §10.1 admits a DELIVERY dispute in ITEM_DELIVERED too — the buyer may
    /// dispute a delivery the platform already concluded. There is nothing left
    /// to transition, and firing the trigger would throw.
    /// </summary>
    [Fact]
    public async Task AlreadyDelivered_Answers_From_The_Evidence_Without_Firing_The_Trigger()
    {
        var transaction = await CreateAsync(
            status: TransactionStatus.ITEM_DELIVERED,
            evidence: DeliveryEvidence.BUYER_CONFIRMED);

        var outcome = await Run(transaction);

        Assert.Equal(DeliveryDisputeOutcome.Delivered, outcome);
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persisted.Status);
        Assert.Empty(_outbox.Published);
        Assert.Empty(await Context.Set<TransactionHistory>().AsNoTracking()
            .Where(h => h.TransactionId == transaction.Id).ToListAsync());
    }

    // ================= Sonuç E — the launch gate =================

    /// <summary>
    /// <b>The deadlock this task closes.</b> Evidence complete, gate closed
    /// (DEPLOY_RUNBOOK §H.2): the round must neither deliver nor answer
    /// "delivered". <c>DeliveryVerifiedAt</c> is the field that actually holds
    /// the gate shut, so stamping it here would release money on an inference no
    /// human has read.
    /// </summary>
    [Fact]
    public async Task GateClosed_Holds_For_Review_Without_Stamping_The_Guard()
    {
        // Observed fresh rather than replayed from recorded flags: the engine
        // short-circuits an already-sufficient transaction and writes no second
        // capture, so driving the conjunction from the inventories is what
        // exercises the gated arm end to end.
        var transaction = await CreateAsync();
        RegisterBuyerCopies("99887766");   // seller's asset unregistered ⇒ gone

        var outcome = await Run(transaction);

        Assert.Equal(DeliveryDisputeOutcome.PendingReview, outcome);
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Null(persisted.DeliveryVerifiedAt);
        Assert.Null(persisted.PayoutEligibleAt);
        Assert.Empty(_outbox.Published);

        // The capture is the row DEPLOY_RUNBOOK §H.3 reads to decide whether the
        // gate may open at all.
        var capture = Assert.Single(await CapturesAsync(transaction.Id));
        Assert.Equal(nameof(DeliveryVerdict.InventoryEvidencePendingReview), capture.Verdict);
        Assert.True(capture.AutoReleaseGated);
    }

    /// <summary>
    /// An emergency hold freezes every trigger (05 §4.5). The evidence is real,
    /// so the round must not answer "not delivered" either — it rolls the stamp
    /// back field by field and reports the same held outcome, which keeps the
    /// dispute open and escalatable. The rollback matters because the dispute
    /// service commits this unit of work regardless of what the round concluded.
    /// </summary>
    [Fact]
    public async Task RefusedTransition_RollsBackTheStamp_AndHolds()
    {
        await OpenLaunchGateAsync();
        var transaction = await CreateAsync(onHold: true);
        RegisterBuyerCopies("99887766");

        var outcome = await Run(transaction);

        Assert.Equal(DeliveryDisputeOutcome.PendingReview, outcome);
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Null(persisted.DeliveryVerifiedAt);
        Assert.Null(persisted.PayoutEligibleAt);
        Assert.Null(persisted.DeliveredBuyerAssetId);
        Assert.Empty(_outbox.Published);

        // The observation itself still survives: flags are what they are.
        Assert.Equal(
            DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA,
            persisted.DeliveryEvidence);
    }

    // ================= Sonuç B / C / D =================

    /// <summary>
    /// 03 §6.2 Sonuç C. The round reports the signature and records the capture,
    /// but does NOT open the dispute itself: on this path the caller is creating
    /// the DELIVERY row in the same unit of work, and
    /// <c>UQ_Disputes_TransactionId_Type</c> permits exactly one.
    /// </summary>
    [Fact]
    public async Task MisdeliverySignature_IsReported_AndCaptured()
    {
        var transaction = await CreateAsync();
        // Nothing registered for either party: the seller's asset is absent from
        // a readable inventory, and the buyer's readable inventory holds nothing.

        var outcome = await Run(transaction);

        Assert.Equal(DeliveryDisputeOutcome.MisdeliverySignature, outcome);
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Equal(DeliveryEvidence.SELLER_ASSET_GONE, persisted.DeliveryEvidence);
        Assert.False(persisted.HasActiveDispute);

        var capture = Assert.Single(await CapturesAsync(transaction.Id));
        Assert.Equal(nameof(DeliveryVerdict.MisdeliverySignature), capture.Verdict);
    }

    /// <summary>03 §6.2 Sonuç B — both sides read, neither moved.</summary>
    [Fact]
    public async Task NoMovement_Reports_NotSent()
    {
        var transaction = await CreateAsync();
        RegisterSellerStillHoldsItem();

        var outcome = await Run(transaction);

        Assert.Equal(DeliveryDisputeOutcome.NotSent, outcome);
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Equal(DeliveryEvidence.NONE, persisted.DeliveryEvidence);
        // A poll that found nothing is not evidence about anything.
        Assert.Empty(await CapturesAsync(transaction.Id));
    }

    /// <summary>
    /// 03 §6.2 Sonuç D. A seller's asset gone with an unreadable buyer side is
    /// NOT the misdelivery signature — 08 §2.3 forbids reading "I could not
    /// look" as a finding against a seller.
    /// </summary>
    [Fact]
    public async Task UnreadableBuyerInventory_Reports_Unreadable_NotASignature()
    {
        var transaction = await CreateAsync();
        _inventory.ForcedBaselineVisibility = InventoryVisibility.Private;

        var outcome = await Run(transaction);

        Assert.Equal(DeliveryDisputeOutcome.Unreadable, outcome);
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Empty(await CapturesAsync(transaction.Id));
    }

    // ================= Cross-cutting invariants =================

    /// <summary>
    /// 02 §10.1 requires this path to run the rules "taze olarak": a cached read
    /// can still show an item the seller traded away two minutes ago.
    /// </summary>
    [Fact]
    public async Task Reads_Both_Inventories_Fresh()
    {
        var transaction = await CreateAsync();
        RegisterSellerStillHoldsItem();

        await Run(transaction);

        Assert.Equal(InventoryReadFreshness.Fresh, Assert.Single(_inventory.ItemReadFreshness));
        Assert.Equal(InventoryReadFreshness.Fresh, Assert.Single(_inventory.BaselineReadFreshness));
    }

    /// <summary>
    /// The rule that separates this round from the timeout's: a dispute is a
    /// question, and answering it by cancelling somebody's transaction would be
    /// a side effect nobody asked for. No arm may produce a terminal state.
    /// </summary>
    [Theory]
    [InlineData(DeliveryEvidence.NONE)]
    [InlineData(DeliveryEvidence.SELLER_ASSET_GONE)]
    [InlineData(DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA)]
    public async Task No_Arm_Ever_Cancels(DeliveryEvidence evidence)
    {
        var transaction = await CreateAsync(evidence: evidence);

        await Run(transaction);
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.True(
            persisted.Status is TransactionStatus.PAYMENT_RECEIVED
                or TransactionStatus.ITEM_DELIVERED,
            $"A dispute round moved the transaction to {persisted.Status}");
        Assert.Null(persisted.CancelledAt);
    }

    // ================= Helpers =================

    private async Task<DeliveryDisputeOutcome> Run(Transaction transaction)
    {
        var tracked = await Context.Set<Transaction>().FirstAsync(t => t.Id == transaction.Id);
        return await BuildSut().RunAsync(tracked, CancellationToken.None);
    }

    private DeliveryDisputeRound BuildSut() =>
        new(Context,
            // The real engine: this round's whole argument is about how it reacts
            // to each verdict, so a stubbed engine would test nothing.
            new DeliveryVerificationService(
                Context, _inventory, NullLogger<DeliveryVerificationService>.Instance, _clock),
            new SettlementSettingsProvider(Context),
            _outbox,
            NullLogger<DeliveryDisputeRound>.Instance,
            _clock);

    private Task OpenLaunchGateAsync() =>
        Context.ConfigureSettingAsync(DeliveryVerificationService.AutoReleaseSettingKey, "true");

    private void RegisterSellerStillHoldsItem() =>
        _inventory.Register(SellerSteamId, NewSnapshot(ItemAssetId));

    private void RegisterBuyerCopies(params string[] assetIds)
    {
        foreach (var assetId in assetIds)
            _inventory.Register(BuyerSteamId, NewSnapshot(assetId));
    }

    private static InventoryItemSnapshot NewSnapshot(string assetId) =>
        new(AssetId: assetId,
            ClassId: ItemClassId,
            InstanceId: ItemInstanceId,
            Name: "AK-47 | Redline",
            MarketHashName: "AK-47 | Redline (Field-Tested)",
            IconUrl: null,
            Exterior: "Field-Tested",
            Type: "Rifle",
            InspectLink: null,
            IsTradeable: true);

    private async Task<List<DeliveryEvidenceCapture>> CapturesAsync(Guid transactionId)
    {
        Context.ChangeTracker.Clear();
        return await Context.Set<DeliveryEvidenceCapture>().AsNoTracking()
            .Where(c => c.TransactionId == transactionId)
            .ToListAsync();
    }

    private async Task<Transaction> ReloadAsync(Guid transactionId)
    {
        Context.ChangeTracker.Clear();
        return await Context.Set<Transaction>().FirstAsync(t => t.Id == transactionId);
    }

    private async Task<Transaction> CreateAsync(
        TransactionStatus status = TransactionStatus.PAYMENT_RECEIVED,
        DeliveryEvidence evidence = DeliveryEvidence.NONE,
        bool onHold = false)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = BuyerSteamId,
            BuyerRefundAddress = ValidWallet2,
            BuyerTradeUrl = BuyerTradeUrl,
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
            AcceptedAt = nowUtc.AddHours(-4),
            SellerReadyConfirmedAt = nowUtc.AddHours(-3),
            PaymentReceivedAt = nowUtc.AddHours(-2),
            DeliveryDeadline = nowUtc.AddHours(2),
            DeliveryEvidence = evidence,
            // Present throughout so the inventory path is genuinely open and each
            // test exercises its own rule rather than a missing snapshot (06 §3.5).
            BuyerBaselineClassCount = 0,
            BuyerBaselineAssetIds = JsonSerializer.Serialize(Array.Empty<string>()),
            BuyerBaselineClassIds = JsonSerializer.Serialize(Array.Empty<string>()),
            BuyerBaselineCapturedAt = nowUtc.AddHours(-3),
            // An already-delivered fixture must satisfy the ITEM_DELIVERED
            // invariants it would have been given on the way in (06 §3.5).
            DeliveryVerifiedAt = status == TransactionStatus.ITEM_DELIVERED
                ? nowUtc.AddHours(-1) : null,
            ItemDeliveredAt = status == TransactionStatus.ITEM_DELIVERED
                ? nowUtc.AddHours(-1) : null,
            PayoutEligibleAt = status == TransactionStatus.ITEM_DELIVERED
                ? nowUtc.AddDays(8) : null,
            // CK_Transactions_Hold + CK_Transactions_FreezeHold_Reverse.
            IsOnHold = onHold,
            EmergencyHoldAt = onHold ? nowUtc.AddMinutes(-5) : null,
            EmergencyHoldReason = onHold ? "Test emergency hold" : null,
            EmergencyHoldByAdminId = onHold ? _admin.Id : null,
            TimeoutFrozenAt = onHold ? nowUtc.AddMinutes(-5) : null,
            TimeoutFreezeReason = onHold ? TimeoutFreezeReason.EMERGENCY_HOLD : null,
            TimeoutRemainingSeconds = onHold ? 3600 : null,
        };
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return transaction;
    }

    private sealed class RecordingOutboxService : IOutboxService
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
