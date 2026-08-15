using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
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
/// T127 — <see cref="DeliveryTimeoutRound"/>, the 05 §4.4 verification round the
/// delivery timeout must run before it may cancel.
/// </summary>
/// <remarks>
/// <para>
/// Every test here is a decision about somebody's money, and the two directions
/// are not symmetric: a wrong cancellation refunds the buyer and records a
/// failure against a seller who may well have delivered, while a wrong hold only
/// delays. The suite is organised around that asymmetry — first what may
/// deliver, then what may NOT cancel, then the cost of repeating a round that
/// already concluded.
/// </para>
/// </remarks>
public class DeliveryTimeoutRoundTests : IntegrationTestBase
{
    static DeliveryTimeoutRoundTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string SellerSteamId = "76561198000000090";
    private const string BuyerSteamId = "76561198000000091";
    private const string AdminSteamId = "76561198000000092";
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
    private RecordingEscalator _escalator = null!;

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

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero));
        _inventory = new FakeSteamInventoryReader();
        _outbox = new RecordingOutboxService();
        _escalator = new RecordingEscalator();
    }

    // ================= What may deliver =================

    /// <summary>
    /// The arm the round exists for (05 §4.4): the deadline passed, but the
    /// evidence says the item arrived, so the transaction advances instead of
    /// being cancelled.
    /// </summary>
    [Fact]
    public async Task Complete_Evidence_Delivers_Instead_Of_Cancelling()
    {
        await OpenLaunchGateAsync();
        var transaction = await CreateOverdueAsync();
        RegisterBuyerCopies("99887766");   // seller's asset unregistered ⇒ gone

        var decision = await Run(transaction);

        Assert.Equal(DeliveryTimeoutDecision.Delivered, decision);
        // The round writes onto the caller's unit of work and never saves — the
        // scanner does, once per batch. Every persistence assertion below is
        // therefore also an assertion that the round left the right things
        // tracked.
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persisted.Status);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, persisted.DeliveryVerifiedAt);
        Assert.Equal(
            DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA,
            persisted.DeliveryEvidence);
        // 06 §3.5 — this route is not the buyer's own word, so the column that
        // records that specific fact stays NULL.
        Assert.Null(persisted.BuyerConfirmedReceiptAt);
        // 06 §8.4 — exactly one asset appeared since the baseline, so it can be
        // named without guessing.
        Assert.Equal("99887766", persisted.DeliveredBuyerAssetId);

        // The audit trail the reputation map reads: SYSTEM actor, because this
        // conclusion is the platform's inference and not a user action.
        var history = await Context.Set<TransactionHistory>().AsNoTracking()
            .SingleAsync(h => h.TransactionId == transaction.Id);
        Assert.Equal(nameof(TransactionTrigger.DeliverItem), history.Trigger);
        Assert.Equal(ActorType.SYSTEM, history.ActorType);
        Assert.Equal(SeedConstants.SystemUserId, history.ActorId);

        var evt = Assert.Single(_outbox.Published.OfType<TransactionStatusChangedEvent>());
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, evt.FromStatus);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, evt.ToStatus);
    }

    /// <summary>
    /// The transition may be refused for a reason the round cannot see (an
    /// emergency hold freezes every trigger, 05 §4.5). What must not survive is
    /// the stamp: this round shares its unit of work with the rest of the scan,
    /// so a leftover <c>DeliveryVerifiedAt</c> would be committed by somebody
    /// else's cancellation — and that field is the one holding the launch gate
    /// shut (DEPLOY_RUNBOOK §H).
    /// </summary>
    [Fact]
    public async Task Refused_Transition_Rolls_The_Delivery_Stamp_Back()
    {
        await OpenLaunchGateAsync();
        var transaction = await CreateOverdueAsync(onHold: true);
        RegisterBuyerCopies("99887766");

        var decision = await Run(transaction);

        Assert.Equal(DeliveryTimeoutDecision.Held, decision);
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Null(persisted.DeliveryVerifiedAt);
        Assert.Null(persisted.DeliveredBuyerAssetId);
        Assert.Empty(_outbox.Published);
    }

    // ================= What may not cancel =================

    /// <summary>
    /// The launch gate (DEPLOY_RUNBOOK §H.2). Evidence complete, gate closed:
    /// the round neither delivers nor cancels. Cancelling would be the plainly
    /// wrong direction — the evidence says the item reached the buyer — and
    /// delivering would release money on an inference no human has read.
    /// </summary>
    [Fact]
    public async Task Gated_Round_Neither_Delivers_Nor_Cancels_And_Leaves_The_Guard_Shut()
    {
        var transaction = await CreateOverdueAsync();   // gate closed (seed default)
        RegisterBuyerCopies("99887766");

        var decision = await Run(transaction);

        Assert.Equal(DeliveryTimeoutDecision.Held, decision);
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Null(persisted.CancelledAt);
        // The evidence IS persisted — only the timestamp waits (T125 F3).
        Assert.Equal(
            DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA,
            persisted.DeliveryEvidence);
        Assert.Null(persisted.DeliveryVerifiedAt);

        // The reviewer's material (DEPLOY_RUNBOOK §H.3 queries exactly this).
        var capture = await Context.Set<DeliveryEvidenceCapture>().AsNoTracking()
            .SingleAsync(c => c.TransactionId == transaction.Id);
        Assert.Equal(nameof(DeliveryVerdict.InventoryEvidencePendingReview), capture.Verdict);
        Assert.True(capture.AutoReleaseGated);
    }

    /// <summary>
    /// 02 §9.2 / §10.1 — "işlem sessizce iptal edilmez, otomatik olarak
    /// dispute'a yükseltilir". The item left the seller and did not arrive: that
    /// is a finding about a seller, and where the item went is an admin's
    /// question rather than a timeout's.
    /// </summary>
    [Fact]
    public async Task Misdelivery_Signature_Escalates_Instead_Of_Cancelling()
    {
        var transaction = await CreateOverdueAsync();
        // Seller's asset unregistered (gone) and the buyer holds nothing new.

        var decision = await Run(transaction);

        Assert.Equal(DeliveryTimeoutDecision.Held, decision);
        Assert.Equal([transaction.Id], _escalator.Escalations);
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Null(persisted.CancelledAt);
        Assert.Equal(DeliveryEvidence.SELLER_ASSET_GONE, persisted.DeliveryEvidence);
        Assert.True(persisted.HasActiveDispute);

        var capture = await Context.Set<DeliveryEvidenceCapture>().AsNoTracking()
            .SingleAsync(c => c.TransactionId == transaction.Id);
        Assert.Equal(nameof(DeliveryVerdict.MisdeliverySignature), capture.Verdict);
    }

    /// <summary>
    /// <b>The money-safety centre of the K4 rule.</b> The seller's asset is gone
    /// and the buyer's inventory cannot be read, so nothing can be concluded —
    /// but something clearly left the seller. Cancelling here would refund a
    /// buyer who is very likely holding the item.
    /// </summary>
    /// <remarks>
    /// This is <see cref="DeliveryVerdict.Inconclusive"/>, not the misdelivery
    /// signature: 02 §10.1 demands both sides be READ before the platform
    /// accuses a seller, and a private buyer inventory is not a reading.
    /// </remarks>
    [Fact]
    public async Task Asset_Gone_With_An_Unreadable_Buyer_Side_Is_Held_Not_Cancelled()
    {
        var transaction = await CreateOverdueAsync();
        _inventory.ForcedBaselineVisibility = InventoryVisibility.Private;

        var decision = await Run(transaction);

        Assert.Equal(DeliveryTimeoutDecision.Held, decision);
        Assert.Empty(_escalator.Escalations);
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Null(persisted.CancelledAt);
    }

    /// <summary>
    /// 08 §2.3 — an unreachable Steam is absence of information, never a
    /// negative finding. A Steam outage is meant to be absorbed by freezing the
    /// phase, not by cancelling into it.
    /// </summary>
    [Fact]
    public async Task Unreadable_Seller_Side_Is_Held_Not_Cancelled()
    {
        var transaction = await CreateOverdueAsync();
        _inventory.ForcedVisibility = InventoryVisibility.Unavailable;
        _inventory.ForcedBaselineVisibility = InventoryVisibility.Unavailable;

        var decision = await Run(transaction);

        Assert.Equal(DeliveryTimeoutDecision.Held, decision);
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.Equal(DeliveryEvidence.NONE, persisted.DeliveryEvidence);
        // A round that saw nothing is not evidence about anything.
        Assert.Empty(await Context.Set<DeliveryEvidenceCapture>().AsNoTracking().ToListAsync());
    }

    // ================= What may cancel =================

    /// <summary>
    /// 03 §4.4 — the seller simply did not send. Both sides read, nothing moved,
    /// so the timeout proceeds and the caller runs the cancellation path.
    /// </summary>
    [Fact]
    public async Task Item_Still_With_The_Seller_Authorises_The_Cancellation()
    {
        var transaction = await CreateOverdueAsync();
        RegisterSellerStillHoldsItem();

        var decision = await Run(transaction);

        Assert.Equal(DeliveryTimeoutDecision.Cancel, decision);
        // The round itself never cancels — it authorises. The shared timeout
        // path owns the transition, the refund event and the reputation refresh.
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, transaction.Status);
        Assert.Empty(_outbox.Published);
    }

    /// <summary>
    /// The other half of the K4 rule: the buyer's inventory being private does
    /// NOT block a cancellation when the item is demonstrably still with the
    /// seller. 02 §9.2 accepts a private buyer inventory and warns them their
    /// own confirmation is then the only route — refusing to cancel here would
    /// lock their money up with no exit but an admin.
    /// </summary>
    [Fact]
    public async Task Private_Buyer_Inventory_Does_Not_Block_A_Provable_Non_Delivery()
    {
        var transaction = await CreateOverdueAsync();
        RegisterSellerStillHoldsItem();
        _inventory.ForcedBaselineVisibility = InventoryVisibility.Private;

        var decision = await Run(transaction);

        Assert.Equal(DeliveryTimeoutDecision.Cancel, decision);
    }

    // ================= Repeating a concluded round =================

    /// <summary>
    /// A held transaction stays overdue forever, so the scanner reaches it on
    /// every pass. Re-deriving a conclusion already on record must not cost two
    /// rate-limited Steam reads each time (08 §2.2).
    /// </summary>
    [Fact]
    public async Task Second_Round_On_A_Gated_Delivery_Spends_No_Steam_Read()
    {
        var transaction = await CreateOverdueAsync(
            evidence: DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA);

        var decision = await Run(transaction);

        Assert.Equal(DeliveryTimeoutDecision.Held, decision);
        Assert.Empty(_inventory.ItemReadFreshness);
        Assert.Empty(_inventory.BaselineReadFreshness);
        // Nor a second capture row for an observation that never happened.
        await Context.SaveChangesAsync();
        Assert.Empty(await Context.Set<DeliveryEvidenceCapture>().AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// And the reason that saving is safe rather than a trap: opening the gate
    /// still releases the transactions it was holding. The short-circuit skips
    /// the READ, not the decision — the gate is re-read every round.
    /// </summary>
    [Fact]
    public async Task Opening_The_Gate_Delivers_A_Held_Transaction_Without_Reading_Steam()
    {
        await OpenLaunchGateAsync();
        var transaction = await CreateOverdueAsync(
            evidence: DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA);

        var decision = await Run(transaction);

        Assert.Equal(DeliveryTimeoutDecision.Delivered, decision);
        Assert.Empty(_inventory.ItemReadFreshness);
        Assert.Empty(_inventory.BaselineReadFreshness);
        await Context.SaveChangesAsync();

        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persisted.Status);
    }

    /// <summary>
    /// The escalation is re-asserted rather than assumed. It is idempotent and
    /// costs one indexed read, and that is what makes a partially committed
    /// escalation self-healing: a pass that recorded the signature but lost its
    /// dispute raises one here instead of skipping past a finding nobody was
    /// ever told about.
    /// </summary>
    /// <remarks>
    /// The fixture is a recorded CAPTURE, not evidence flags — that is the
    /// distinction finding B1 turned on. Its twin,
    /// <see cref="Second_Round_On_An_Unread_Buyer_Side_Still_Does_Not_Escalate"/>,
    /// carries identical flags and must NOT reach this path.
    /// </remarks>
    [Fact]
    public async Task Second_Round_On_A_Misdelivery_Re_Asserts_The_Escalation_Without_Reading_Steam()
    {
        var transaction = await CreateOverdueAsync(
            evidence: DeliveryEvidence.SELLER_ASSET_GONE);
        await RecordCaptureAsync(transaction.Id, DeliveryVerdict.MisdeliverySignature);

        var decision = await Run(transaction);

        Assert.Equal(DeliveryTimeoutDecision.Held, decision);
        Assert.Equal([transaction.Id], _escalator.Escalations);
        Assert.Empty(_inventory.ItemReadFreshness);
        Assert.Empty(_inventory.BaselineReadFreshness);
    }

    /// <summary>
    /// <b>Finding B1 — the round must not apply on pass two the conclusion the
    /// engine refused on pass one.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seller's asset is gone and the buyer's inventory is private. The
    /// engine verdicts <see cref="DeliveryVerdict.Inconclusive"/> — 02 §10.1
    /// wants both sides READ before the platform accuses a seller — and the
    /// round holds. But the observation is persisted, and
    /// <c>SELLER_ASSET_GONE</c> without <c>INVENTORY_DELTA</c> is precisely what
    /// <c>IsMisdeliverySignature()</c> tests, so a re-entry gate reading the
    /// flags would fire on the very next pass and open a dispute against a
    /// seller who may well have delivered — 30 seconds after the round that
    /// deliberately declined to.
    /// </para>
    /// <para>
    /// Both passes run the full engine here. That is the point: nothing was
    /// concluded, so nothing may be short-circuited.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Second_Round_On_An_Unread_Buyer_Side_Still_Does_Not_Escalate()
    {
        var transaction = await CreateOverdueAsync();
        _inventory.ForcedBaselineVisibility = InventoryVisibility.Private;

        Assert.Equal(DeliveryTimeoutDecision.Held, await Run(transaction));
        await Context.SaveChangesAsync();

        // The flags the old gate read are on record — the fixture is real.
        var afterFirstRound = await ReloadAsync(transaction.Id);
        Assert.True(afterFirstRound.DeliveryEvidence.IsMisdeliverySignature());
        Assert.Empty(await Context.Set<DeliveryEvidenceCapture>().AsNoTracking().ToListAsync());

        _clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(DeliveryTimeoutDecision.Held, await Run(transaction));
        await Context.SaveChangesAsync();

        Assert.Empty(_escalator.Escalations);
        var persisted = await ReloadAsync(transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
        Assert.False(persisted.HasActiveDispute);
        Assert.Null(persisted.CancelledAt);
    }

    /// <summary>
    /// The scanner's fairness window (finding B2) is ordered by this stamp, so
    /// every round must write it — including the arms that conclude nothing,
    /// which are exactly the rows that would otherwise never yield their slot.
    /// </summary>
    [Fact]
    public async Task Every_Round_Stamps_When_The_Row_Was_Last_Examined()
    {
        var transaction = await CreateOverdueAsync();
        _inventory.ForcedVisibility = InventoryVisibility.Unavailable;
        _inventory.ForcedBaselineVisibility = InventoryVisibility.Unavailable;

        var firstRoundAt = _clock.GetUtcNow().UtcDateTime;
        Assert.Equal(DeliveryTimeoutDecision.Held, await Run(transaction));
        await Context.SaveChangesAsync();
        Assert.Equal(firstRoundAt, (await ReloadAsync(transaction.Id)).DeliveryRoundAt);

        _clock.Advance(TimeSpan.FromMinutes(20));
        var secondRoundAt = _clock.GetUtcNow().UtcDateTime;

        Assert.Equal(DeliveryTimeoutDecision.Held, await Run(transaction));
        await Context.SaveChangesAsync();
        Assert.Equal(secondRoundAt, (await ReloadAsync(transaction.Id)).DeliveryRoundAt);
    }

    /// <summary>
    /// And the short-circuits stamp too — a row an admin already owns is the
    /// most persistent occupant of the window, so it is the one that must
    /// hand its slot back.
    /// </summary>
    [Fact]
    public async Task A_Short_Circuited_Round_Stamps_The_Examination_Too()
    {
        var transaction = await CreateOverdueAsync(
            evidence: DeliveryEvidence.SELLER_ASSET_GONE);
        await RecordCaptureAsync(transaction.Id, DeliveryVerdict.MisdeliverySignature);

        Assert.Equal(DeliveryTimeoutDecision.Held, await Run(transaction));
        await Context.SaveChangesAsync();

        Assert.Equal(
            _clock.GetUtcNow().UtcDateTime,
            (await ReloadAsync(transaction.Id)).DeliveryRoundAt);
    }

    /// <summary>
    /// 02 §10.1 requires the dispute path to re-run the rules "taze olarak", and
    /// the same reasoning binds harder here: this round decides whether money
    /// moves, and the sidecar's 120-second cache can still show an item the
    /// seller traded away two minutes ago.
    /// </summary>
    [Fact]
    public async Task Round_Reads_Steam_Fresh()
    {
        var transaction = await CreateOverdueAsync();
        RegisterSellerStillHoldsItem();

        await Run(transaction);

        Assert.Equal([InventoryReadFreshness.Fresh], _inventory.ItemReadFreshness);
        Assert.Equal([InventoryReadFreshness.Fresh], _inventory.BaselineReadFreshness);
    }

    // ================= Helpers =================

    private async Task<DeliveryTimeoutDecision> Run(Transaction transaction)
    {
        var tracked = await Context.Set<Transaction>().FirstAsync(t => t.Id == transaction.Id);
        return await BuildSut().RunAsync(tracked, CancellationToken.None);
    }

    private DeliveryTimeoutRound BuildSut() =>
        new(Context,
            // The real engine: this round's entire safety argument is about how
            // it reacts to each verdict, so a stubbed engine would test nothing.
            new DeliveryVerificationService(
                Context, _inventory, NullLogger<DeliveryVerificationService>.Instance, _clock),
            _escalator,
            _outbox,
            NullLogger<DeliveryTimeoutRound>.Instance,
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

    private async Task<Transaction> ReloadAsync(Guid transactionId)
    {
        Context.ChangeTracker.Clear();
        return await Context.Set<Transaction>().FirstAsync(t => t.Id == transactionId);
    }

    /// <summary>
    /// Seed the record of a conclusion an earlier round reached — the row the
    /// re-entry gate reads (06 §3.5a).
    /// </summary>
    private async Task RecordCaptureAsync(Guid transactionId, DeliveryVerdict verdict)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        Context.Set<DeliveryEvidenceCapture>().Add(new DeliveryEvidenceCapture
        {
            TransactionId = transactionId,
            ObservedAt = nowUtc,
            Verdict = verdict.ToString(),
            Evidence = DeliveryEvidence.SELLER_ASSET_GONE,
            AutoReleaseGated = false,
            Payload = "{}",
            CreatedAt = nowUtc,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    /// <summary>
    /// A transaction whose delivery window has already elapsed — the state the
    /// scanner hands this round.
    /// </summary>
    private async Task<Transaction> CreateOverdueAsync(
        DeliveryEvidence evidence = DeliveryEvidence.NONE,
        bool onHold = false)
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
            DeliveryDeadline = nowUtc.AddMinutes(-1),
            DeliveryEvidence = evidence,
            // The baseline is present throughout so the inventory path is
            // genuinely open and each test exercises its own rule rather than a
            // missing snapshot (06 §3.5).
            BuyerBaselineClassCount = 0,
            BuyerBaselineAssetIds = JsonSerializer.Serialize(Array.Empty<string>()),
            BuyerBaselineCapturedAt = nowUtc.AddHours(-3),
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

    /// <summary>
    /// Records the port calls. What the real adapter WRITES is
    /// <c>MisdeliveryDisputeEscalatorTests</c> in the Disputes suite — the
    /// module direction (Disputes → Transactions) keeps it out of reach here,
    /// which is exactly why the escalation is a port.
    /// </summary>
    private sealed class RecordingEscalator : IDeliveryMisdeliveryEscalator
    {
        public List<Guid> Escalations { get; } = [];

        public Task<MisdeliveryEscalationOutcome> EscalateAsync(
            Transaction transaction, DateTime occurredAtUtc, CancellationToken cancellationToken)
        {
            Escalations.Add(transaction.Id);
            transaction.HasActiveDispute = true;
            return Task.FromResult(MisdeliveryEscalationOutcome.Opened);
        }
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
