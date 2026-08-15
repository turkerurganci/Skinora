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
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Transactions.Tests.Integration.Lifecycle;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Delivery;

/// <summary>
/// T126 — end-to-end coverage for <see cref="DeliveryConfirmationService"/>
/// (07 §7.6b, 03 §3.5). Covers the two party/state gates, the idempotent
/// repeat, the <c>PAYMENT_RECEIVED → ITEM_DELIVERED</c> transition and — the
/// part that moves money — the launch-gate invariant from the T125 validation
/// (finding F3).
/// </summary>
public class DeliveryConfirmationServiceTests : IntegrationTestBase
{
    static DeliveryConfirmationServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string SellerSteamId = "76561198000000090";
    private const string BuyerSteamId = "76561198000000091";
    private const string StrangerSteamId = "76561198000000092";
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
    private User _stranger = null!;
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
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = BuyerSteamId,
            SteamDisplayName = "Buyer",
        };
        _stranger = new User
        {
            Id = Guid.NewGuid(),
            SteamId = StrangerSteamId,
            SteamDisplayName = "Stranger",
        };
        context.Set<User>().AddRange(_seller, _buyer, _stranger);
        await context.SaveChangesAsync();

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero));
        _inventory = new FakeSteamInventoryReader();
        _outbox = new RecordingOutboxService();
    }

    // ================= Happy path =================

    /// <summary>
    /// 02 §9.2 — the buyer's confirmation is sufficient evidence on its own, so
    /// the transaction delivers immediately.
    /// </summary>
    [Fact]
    public async Task Buyer_Confirmation_Delivers_The_Transaction()
    {
        var transaction = await CreateTransactionAsync();

        var outcome = await BuildSut().ConfirmReceiptAsync(
            _buyer.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReceiptStatus.Confirmed, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, outcome.Body!.Status);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, outcome.Body.DeliveryVerifiedAt);
        Assert.Equal(new[] { "BUYER_CONFIRMED" }, outcome.Body.Evidence);

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persisted.Status);
        Assert.Equal(DeliveryEvidence.BUYER_CONFIRMED, persisted.DeliveryEvidence);
        Assert.NotNull(persisted.DeliveryVerifiedAt);
        // Stamped by the ITEM_DELIVERED OnEntry, which is what T129's settlement
        // window will later be measured from (02 §4.5.1).
        Assert.NotNull(persisted.ItemDeliveredAt);
    }

    /// <summary>
    /// The round runs (02 §9.2 requires the rules to execute when the buyer
    /// confirms) but spends no Steam call: <c>BUYER_CONFIRMED</c> is merged
    /// first, so the engine short-circuits. Two rate-limited round trips per
    /// confirmation would buy a weaker signal arguing with a stronger one.
    /// </summary>
    [Fact]
    public async Task Buyer_Confirmation_Spends_No_Steam_Reads()
    {
        var transaction = await CreateTransactionAsync();

        await BuildSut().ConfirmReceiptAsync(_buyer.Id, transaction.Id, CancellationToken.None);

        Assert.Empty(_inventory.ItemReadFreshness);
        Assert.Empty(_inventory.BaselineReadFreshness);
    }

    /// <summary>
    /// 06 §3.6 audit trail + the realtime relay 03 §3.5 step 9 relies on. There
    /// is deliberately no notification type for ITEM_DELIVERED — the status
    /// event is the whole delivery-side surface.
    /// </summary>
    [Fact]
    public async Task Buyer_Confirmation_Writes_History_And_Publishes_The_Status_Change()
    {
        var transaction = await CreateTransactionAsync();

        await BuildSut().ConfirmReceiptAsync(_buyer.Id, transaction.Id, CancellationToken.None);

        var history = await Context.Set<TransactionHistory>()
            .AsNoTracking()
            .Where(h => h.TransactionId == transaction.Id)
            .ToListAsync();
        var row = Assert.Single(history);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, row.PreviousStatus);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, row.NewStatus);
        Assert.Equal(nameof(TransactionTrigger.DeliverItem), row.Trigger);
        // The buyer acted, not the system — this is the one delivery path that
        // has a human behind it.
        Assert.Equal(ActorType.USER, row.ActorType);
        Assert.Equal(_buyer.Id, row.ActorId);

        var published = Assert.Single(_outbox.Published);
        var statusChanged = Assert.IsType<TransactionStatusChangedEvent>(published);
        Assert.Equal(transaction.Id, statusChanged.TransactionId);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, statusChanged.FromStatus);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, statusChanged.ToStatus);
    }

    /// <summary>
    /// The launch-gate capture table stays empty on this path: a buyer-confirmed
    /// round observes no inventory, so it is not evidence about anything a
    /// reviewer would read (DEPLOY_RUNBOOK §H.3).
    /// </summary>
    [Fact]
    public async Task Buyer_Confirmation_Records_No_Evidence_Capture()
    {
        var transaction = await CreateTransactionAsync();

        await BuildSut().ConfirmReceiptAsync(_buyer.Id, transaction.Id, CancellationToken.None);

        Assert.Empty(await Context.Set<DeliveryEvidenceCapture>().AsNoTracking().ToListAsync());
    }

    // ================= Party guard (07 §7.6b) =================

    /// <summary>
    /// The seller is refused. Their claim to have sent the item is not evidence
    /// under 02 §9.2 — which is the entire reason the inventory path exists.
    /// </summary>
    [Fact]
    public async Task Seller_Cannot_Confirm_Receipt()
    {
        var transaction = await CreateTransactionAsync();

        var outcome = await BuildSut().ConfirmReceiptAsync(
            _seller.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReceiptStatus.NotAParty, outcome.Status);
        Assert.Equal(TransactionErrorCodes.NotAParty, outcome.ErrorCode);
        await AssertUntouchedAsync(transaction.Id);
    }

    [Fact]
    public async Task Third_Party_Cannot_Confirm_Receipt()
    {
        var transaction = await CreateTransactionAsync();

        var outcome = await BuildSut().ConfirmReceiptAsync(
            _stranger.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReceiptStatus.NotAParty, outcome.Status);
        await AssertUntouchedAsync(transaction.Id);
    }

    /// <summary>
    /// The party guard runs BEFORE the state guard, so probing arbitrary ids
    /// leaks nothing about which state a transaction is in.
    /// </summary>
    [Fact]
    public async Task Party_Guard_Answers_Before_The_State_Guard()
    {
        var transaction = await CreateTransactionAsync(status: TransactionStatus.ACCEPTED);

        var outcome = await BuildSut().ConfirmReceiptAsync(
            _stranger.Id, transaction.Id, CancellationToken.None);

        // Not InvalidStateTransition — a stranger must not learn the state.
        Assert.Equal(ConfirmReceiptStatus.NotAParty, outcome.Status);
    }

    [Fact]
    public async Task Unknown_Transaction_Returns_NotFound()
    {
        var outcome = await BuildSut().ConfirmReceiptAsync(
            _buyer.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ConfirmReceiptStatus.NotFound, outcome.Status);
        Assert.Equal(TransactionErrorCodes.TransactionNotFound, outcome.ErrorCode);
    }

    // ================= State guard (07 §7.6b) =================

    /// <summary>
    /// Only PAYMENT_RECEIVED can deliver. COMPLETED and CANCELLED_TIMEOUT are in
    /// the list on purpose: both sit past a delivery decision, and answering 200
    /// there would read as this call having confirmed something (the idempotent
    /// branch is scoped to ITEM_DELIVERED alone).
    /// </summary>
    [Theory]
    // CREATED carries a buyer here although a real one would not: the party
    // guard runs first, and without a buyer this fixture would never reach the
    // state guard the test is about.
    [InlineData(TransactionStatus.CREATED)]
    [InlineData(TransactionStatus.ACCEPTED)]
    [InlineData(TransactionStatus.SELLER_CONFIRMED)]
    [InlineData(TransactionStatus.COMPLETED)]
    [InlineData(TransactionStatus.CANCELLED_TIMEOUT)]
    public async Task Confirming_Outside_PaymentReceived_Is_Refused(TransactionStatus status)
    {
        var transaction = await CreateTransactionAsync(status: status);

        var outcome = await BuildSut().ConfirmReceiptAsync(
            _buyer.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReceiptStatus.InvalidStateTransition, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InvalidStateTransition, outcome.ErrorCode);

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(status, persisted.Status);
        Assert.Equal(DeliveryEvidence.NONE, persisted.DeliveryEvidence);
        Assert.Empty(_outbox.Published);
    }

    /// <summary>
    /// An emergency hold freezes every trigger (05 §4.5) and reports its own
    /// code, so the buyer is told the transaction is frozen rather than that
    /// their confirmation was out of order.
    /// </summary>
    [Fact]
    public async Task Emergency_Hold_Refuses_With_Its_Own_Code()
    {
        var transaction = await CreateTransactionAsync(onHold: true);

        var outcome = await BuildSut().ConfirmReceiptAsync(
            _buyer.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReceiptStatus.InvalidStateTransition, outcome.Status);
        Assert.Equal(TransactionStateMachine.OnHoldErrorCode, outcome.ErrorCode);
        await AssertUntouchedAsync(transaction.Id);
    }

    // ================= Idempotency (07 §7.6b) =================

    /// <summary>
    /// A repeat returns the same answer and writes nothing a second time — the
    /// buyer double-clicking must not produce two history rows or two events.
    /// </summary>
    [Fact]
    public async Task Second_Call_Returns_The_Same_Answer_Without_Writing_Again()
    {
        var transaction = await CreateTransactionAsync();

        var first = await BuildSut().ConfirmReceiptAsync(
            _buyer.Id, transaction.Id, CancellationToken.None);
        Context.ChangeTracker.Clear();

        // Time moves on so a second stamp would be visible if one happened.
        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = await BuildSut().ConfirmReceiptAsync(
            _buyer.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReceiptStatus.Confirmed, first.Status);
        Assert.Equal(ConfirmReceiptStatus.AlreadyDelivered, second.Status);
        Assert.Equal(first.Body!.DeliveryVerifiedAt, second.Body!.DeliveryVerifiedAt);
        Assert.Equal(first.Body.Evidence, second.Body.Evidence);

        Assert.Single(await Context.Set<TransactionHistory>()
            .AsNoTracking().Where(h => h.TransactionId == transaction.Id).ToListAsync());
        Assert.Single(_outbox.Published);
    }

    /// <summary>
    /// The idempotent branch also covers a delivery the platform concluded from
    /// inventories: the buyer arrives late, the state is already ITEM_DELIVERED,
    /// and the recorded evidence is returned as-is rather than being amended.
    /// </summary>
    [Fact]
    public async Task Already_Delivered_By_Inventory_Evidence_Returns_The_Recorded_Evidence()
    {
        var transaction = await CreateTransactionAsync(
            status: TransactionStatus.ITEM_DELIVERED,
            evidence: DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA);

        var outcome = await BuildSut().ConfirmReceiptAsync(
            _buyer.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReceiptStatus.AlreadyDelivered, outcome.Status);
        // Ordinal order of the [Flags] members, not observation order.
        Assert.Equal(new[] { "INVENTORY_DELTA", "SELLER_ASSET_GONE" }, outcome.Body!.Evidence);

        // No mutation: an idempotent repeat records nothing, so BUYER_CONFIRMED
        // is NOT bolted onto a delivery the buyer never confirmed.
        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(
            DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA,
            persisted.DeliveryEvidence);
        Assert.Empty(_outbox.Published);
    }

    // ================= Launch gate (T125 validation finding F3) =================

    /// <summary>
    /// <b>The invariant.</b> With the launch gate closed and the inventory
    /// conjunction complete, a round is gated — and the field that actually
    /// holds the gate shut is <c>DeliveryVerifiedAt</c>. The state-machine guard
    /// <c>HasDeliveryEvidence()</c> knows nothing about the gate, so a caller
    /// that persists the evidence AND stamps the timestamp would release money
    /// on an inference no human has read (02 §9.2, DEPLOY_RUNBOOK §H).
    /// </summary>
    [Fact]
    public async Task Gated_Round_Leaves_The_Delivery_Guard_Shut()
    {
        // Gate closed (the 06 §8 seed default) + both evidence bits available:
        // the seller's asset is absent from their inventory and the buyer now
        // holds a copy they did not hold at baseline.
        var created = await CreateTransactionAsync();
        RegisterBuyerCopies("99887766");
        var transaction = await Context.Set<Transaction>().FirstAsync(t => t.Id == created.Id);

        var result = await BuildVerificationService().VerifyAsync(
            transaction, InventoryReadFreshness.Fresh, CancellationToken.None);

        Assert.True(result.AutoReleaseGated);
        Assert.True(result.Evidence.IsSufficientForDelivery());

        // What a gated caller is allowed to do: persist the evidence, withhold
        // the timestamp (T127 does exactly this).
        transaction.DeliveryEvidence = result.Evidence;
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Null(persisted.DeliveryVerifiedAt);
        Assert.False(new TransactionStateMachine(persisted).CanFire(TransactionTrigger.DeliverItem));

        // And the causality, so this test fails for the right reason: stamping
        // the timestamp is the single step that would have opened the guard.
        persisted.DeliveryVerifiedAt = _clock.GetUtcNow().UtcDateTime;
        Assert.True(new TransactionStateMachine(persisted).CanFire(TransactionTrigger.DeliverItem));
    }

    /// <summary>
    /// The other half of the invariant: the gate governs the platform's
    /// inference, never the buyer's own decision. Same closed gate, same
    /// complete inventory evidence — the buyer's confirmation still delivers.
    /// </summary>
    [Fact]
    public async Task Closed_Gate_Does_Not_Restrain_The_Buyer_Path()
    {
        // The state a gated round leaves behind: evidence recorded, timestamp
        // withheld, transaction parked in PAYMENT_RECEIVED.
        var transaction = await CreateTransactionAsync(
            evidence: DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA);
        RegisterBuyerCopies("99887766");

        var outcome = await BuildSut().ConfirmReceiptAsync(
            _buyer.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReceiptStatus.Confirmed, outcome.Status);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, outcome.Body!.Status);

        var persisted = await ReloadAsync(transaction.Id);
        Assert.NotNull(persisted.DeliveryVerifiedAt);
        Assert.Equal(
            DeliveryEvidence.BUYER_CONFIRMED
                | DeliveryEvidence.SELLER_ASSET_GONE
                | DeliveryEvidence.INVENTORY_DELTA,
            persisted.DeliveryEvidence);
    }

    /// <summary>
    /// The defensive branch. Merging <c>BUYER_CONFIRMED</c> before the round
    /// makes a gated verdict structurally impossible here today, so the engine
    /// is replaced by one replaying a genuinely gated result. The endpoint must
    /// refuse rather than stamp: the assumption that keeps this path safe lives
    /// in another class, and if it ever changes the money must not move.
    /// </summary>
    [Fact]
    public async Task Gated_Verdict_Refuses_Instead_Of_Stamping()
    {
        // A real gated result, produced by the real engine rather than hand-built.
        var probe = await CreateTransactionAsync();
        RegisterBuyerCopies("99887766");
        var gated = await BuildVerificationService().VerifyAsync(
            probe, InventoryReadFreshness.Fresh, CancellationToken.None);
        Assert.True(gated.AutoReleaseGated);
        Context.ChangeTracker.Clear();

        var transaction = await CreateTransactionAsync(itemAssetId: "27348562892");
        var outcome = await BuildSut(new ReplayingVerificationService(gated))
            .ConfirmReceiptAsync(_buyer.Id, transaction.Id, CancellationToken.None);

        Assert.Equal(ConfirmReceiptStatus.InvalidStateTransition, outcome.Status);

        // Nothing was written — not the transition, not the timestamp, not even
        // the confirmation flag, so a retry sees the state it started from.
        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Null(persisted.DeliveryVerifiedAt);
        Assert.Equal(DeliveryEvidence.NONE, persisted.DeliveryEvidence);
        Assert.Empty(_outbox.Published);
        Assert.Empty(await Context.Set<TransactionHistory>()
            .AsNoTracking().Where(h => h.TransactionId == transaction.Id).ToListAsync());
    }

    // ================= helpers =================

    private DeliveryConfirmationService BuildSut(IDeliveryVerificationService? verification = null) =>
        new(
            Context,
            // The real engine by default: this endpoint's whole safety argument
            // rests on how the engine answers a buyer-confirmed round, so a stub
            // would test the wrong thing.
            verification ?? BuildVerificationService(),
            _outbox,
            NullLogger<DeliveryConfirmationService>.Instance,
            _clock);

    private DeliveryVerificationService BuildVerificationService() =>
        new(Context, _inventory, NullLogger<DeliveryVerificationService>.Instance, _clock);

    private async Task<Transaction> ReloadAsync(Guid transactionId)
    {
        Context.ChangeTracker.Clear();
        return await Context.Set<Transaction>().FirstAsync(t => t.Id == transactionId);
    }

    private async Task AssertUntouchedAsync(Guid transactionId)
    {
        var persisted = await ReloadAsync(transactionId);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Null(persisted.DeliveryVerifiedAt);
        Assert.Equal(DeliveryEvidence.NONE, persisted.DeliveryEvidence);
        Assert.Empty(_outbox.Published);
    }

    /// <summary>Register the buyer's current copies of the transaction's item class.</summary>
    private void RegisterBuyerCopies(params string[] assetIds)
    {
        foreach (var assetId in assetIds)
        {
            _inventory.Register(BuyerSteamId, new InventoryItemSnapshot(
                AssetId: assetId,
                ClassId: ItemClassId,
                InstanceId: ItemInstanceId,
                Name: "AK-47 | Redline",
                MarketHashName: "AK-47 | Redline (Field-Tested)",
                IconUrl: null,
                Exterior: "Field-Tested",
                Type: "Rifle",
                InspectLink: null,
                IsTradeable: true));
        }
    }

    private async Task<Transaction> CreateTransactionAsync(
        TransactionStatus status = TransactionStatus.PAYMENT_RECEIVED,
        DeliveryEvidence evidence = DeliveryEvidence.NONE,
        bool onHold = false,
        // UQ_Transactions_SellerId_ItemAssetId_Active — one seller cannot have
        // two active transactions on the same asset, so a test needing a second
        // fixture has to name a different one.
        string itemAssetId = ItemAssetId)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var isCancelled = status is TransactionStatus.CANCELLED_TIMEOUT
            or TransactionStatus.CANCELLED_SELLER
            or TransactionStatus.CANCELLED_BUYER
            or TransactionStatus.CANCELLED_ADMIN
            or TransactionStatus.REFUNDED;
        var isDelivered = status is TransactionStatus.ITEM_DELIVERED or TransactionStatus.COMPLETED;

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = BuyerSteamId,
            BuyerRefundAddress = ValidWallet2,
            // 06 §3.5 counts this NOT NULL from ACCEPTED onwards and the
            // DeliverItem guard reaches it through HasFieldsForAccepted(), so a
            // fixture without it can never deliver.
            BuyerTradeUrl = BuyerTradeUrl,
            ItemAssetId = itemAssetId,
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
            PaymentReceivedAt = nowUtc.AddMinutes(-30),
            DeliveryDeadline = nowUtc.AddHours(23),
            DeliveryEvidence = evidence,
            // 06 §3.5 keeps the three baseline columns NULL together; here they
            // are always present so the inventory path is genuinely open and the
            // gate tests exercise the gate rather than a missing snapshot.
            BuyerBaselineClassCount = 0,
            BuyerBaselineAssetIds = JsonSerializer.Serialize(Array.Empty<string>()),
            BuyerBaselineCapturedAt = nowUtc.AddHours(-1),
            // A transaction already past delivery carries the trail that got it
            // there (the DeliverItem guard + its OnEntry).
            DeliveryVerifiedAt = isDelivered ? nowUtc.AddMinutes(-10) : null,
            ItemDeliveredAt = isDelivered ? nowUtc.AddMinutes(-10) : null,
            CompletedAt = status == TransactionStatus.COMPLETED ? nowUtc.AddMinutes(-1) : null,
            // CK_Transactions_Cancel — the terminal cancel/refund states require
            // the full forensic trio.
            CancelledAt = isCancelled ? nowUtc.AddMinutes(-1) : null,
            CancelledBy = isCancelled ? CancelledByType.TIMEOUT : null,
            CancelReason = isCancelled ? "Test iptal sebebi (>=10 char)" : null,
            // CK_Transactions_Hold + CK_Transactions_FreezeHold_Reverse — a held
            // transaction is also a frozen one, with EMERGENCY_HOLD as reason.
            IsOnHold = onHold,
            EmergencyHoldAt = onHold ? nowUtc.AddMinutes(-5) : null,
            EmergencyHoldReason = onHold ? "Test emergency hold" : null,
            EmergencyHoldByAdminId = onHold ? _stranger.Id : null,
            TimeoutFrozenAt = onHold ? nowUtc.AddMinutes(-5) : null,
            TimeoutFreezeReason = onHold ? TimeoutFreezeReason.EMERGENCY_HOLD : null,
            TimeoutRemainingSeconds = onHold ? 3600 : null,
        };
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return transaction;
    }

    /// <summary>
    /// Replays a pre-computed verification result whatever it is handed. Used to
    /// drive the launch-gate refusal, which the production ordering makes
    /// unreachable through the engine itself.
    /// </summary>
    private sealed class ReplayingVerificationService : IDeliveryVerificationService
    {
        private readonly DeliveryVerificationResult _result;

        public ReplayingVerificationService(DeliveryVerificationResult result) => _result = result;

        public Task<DeliveryVerificationResult> VerifyAsync(
            Transaction transaction,
            InventoryReadFreshness freshness,
            CancellationToken cancellationToken)
            => Task.FromResult(_result);
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
