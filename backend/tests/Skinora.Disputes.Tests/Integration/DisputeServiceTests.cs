using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Disputes.Application.AutoCheckers;
using Skinora.Disputes.Application.Disputes;
using Skinora.Disputes.Domain.Entities;
using Skinora.Disputes.Infrastructure.Persistence;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Application.Delivery;
using Skinora.Transactions.Application.Settlement;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Disputes.Tests.Integration;

/// <summary>
/// End-to-end coverage for <see cref="DisputeService"/> (T58 — 02 §10,
/// 03 §6, 07 §7.8–§7.10). Exercises every type-specific auto-checker and
/// every error code listed under 07 §7.8–§7.10 "Hatalar" against a real
/// SQL Server instance.
/// </summary>
public class DisputeServiceTests : IntegrationTestBase
{
    static DisputeServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
        DisputesModuleDbRegistration.RegisterDisputesModule();

        // T130 — the delivery auto-check now runs the real 02 §9.2 round, which
        // reads the launch gate out of SystemSettings (DEPLOY_RUNBOOK §H).
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string SellerSteam = "76561198000000301";
    private const string BuyerSteam = "76561198000000302";
    private const string SellerWallet = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL";

    private FakeTimeProvider _clock = null!;
    private RecordingOutboxService _outbox = null!;
    private FakeInventoryReader _inventory = null!;
    private User _seller = null!;
    private User _buyer = null!;
    private User _otherUser = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 5, 12, 0, 0, TimeSpan.Zero));
        _outbox = new RecordingOutboxService();
        _inventory = new FakeInventoryReader();

        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteam,
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = SellerWallet,
            MobileAuthenticatorVerified = true,
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = BuyerSteam,
            SteamDisplayName = "Buyer",
            MobileAuthenticatorVerified = true,
        };
        _otherUser = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000999",
            SteamDisplayName = "Stranger",
        };
        context.Set<User>().AddRange(_seller, _buyer, _otherUser);

        await context.SaveChangesAsync();
    }

    // ---------- Open ▸ PAYMENT ----------

    [Fact]
    public async Task Open_Payment_NoConfirmedPayment_StaysOpen_AndFlagsTransaction()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED);
        var sut = BuildSut();

        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.Opened, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Equal(DisputeStatus.OPEN, outcome.Body.Status);
        Assert.False(outcome.Body.AutoCheckResult.Resolved);
        Assert.True(outcome.Body.AutoCheckResult.CanSubmitTxHash);
        Assert.True(outcome.Body.AutoCheckResult.CanEscalate);

        var persisted = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == outcome.Body.Id);
        Assert.Equal(DisputeStatus.OPEN, persisted.Status);
        Assert.Null(persisted.ResolvedAt);
        Assert.Equal(DisputeType.PAYMENT, persisted.Type);

        var refreshedTx = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.True(refreshedTx.HasActiveDispute);

        Assert.Empty(_outbox.Published);
    }

    // WP17 — a non-default buyer locale flows through DisputeService end-to-end
    // (proves the ResolveLocaleAsync -> DisputeAutoCheckMessages.Localize wiring,
    // not just the "en" default the other tests exercise).
    [Fact]
    public async Task Open_Then_Escalate_BuyerLocaleTr_LocalizesStoredResponseAndEvent()
    {
        var buyer = await Context.Set<User>().FirstAsync(u => u.Id == _buyer.Id);
        buyer.PreferredLanguage = "tr";
        await Context.SaveChangesAsync();

        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED);
        var sut = BuildSut();

        // Open PAYMENT (no confirmed payment) → unresolved, Turkish message stored + returned.
        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal("Blockchain üzerinde ödeme bulunamadı", open.Body!.AutoCheckResult.Message);

        var persisted = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == open.Body.Id);
        Assert.Equal("Blockchain üzerinde ödeme bulunamadı", persisted.SystemCheckResult);

        // Escalate → response message AND the emitted event OutcomeText both Turkish.
        var esc = await sut.EscalateAsync(_buyer.Id, tx.Id, open.Body.Id,
            new EscalateDisputeRequest("Ödemeyi gönderdim ama sistem görmüyor"),
            CancellationToken.None);
        Assert.Equal("İtirazınız admin ekibine iletildi", esc.Body!.Message);

        var escEvent = Assert.Single(_outbox.Published.OfType<DisputeEscalatedEvent>());
        Assert.Equal("İtirazınız admin ekibine iletildi", escEvent.OutcomeText);
    }

    [Fact]
    public async Task Open_Payment_ConfirmedPaymentExists_AutoResolves_AndEmitsEvent()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED);
        await SeedConfirmedBuyerPaymentAsync(tx, "abc123def456789012345678");

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.Opened, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Equal(DisputeStatus.CLOSED, outcome.Body.Status);
        Assert.True(outcome.Body.AutoCheckResult.Resolved);
        Assert.False(outcome.Body.AutoCheckResult.CanSubmitTxHash);
        Assert.False(outcome.Body.AutoCheckResult.CanEscalate);

        var refreshedTx = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.False(refreshedTx.HasActiveDispute);

        var resolvedEvent = Assert.Single(_outbox.Published.OfType<DisputeAutoResolvedEvent>());
        Assert.Equal(outcome.Body.Id, resolvedEvent.DisputeId);
        Assert.Equal(_buyer.Id, resolvedEvent.BuyerId);
    }

    // ---------- Open ▸ DELIVERY ----------

    [Fact]
    public async Task Open_Delivery_BuyerConfirmedEvidence_AutoResolves()
    {
        // v3.0 — the platform no longer sends or tracks trade offers, so the
        // auto-checker reads the recorded delivery evidence instead (02 §9.2).
        // The buyer's own confirmation is sufficient on its own.
        var tx = await CreateTransactionAsync(
            TransactionStatus.ITEM_DELIVERED,
            deliveryEvidence: DeliveryEvidence.BUYER_CONFIRMED);

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.DELIVERY), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.Opened, outcome.Status);
        Assert.Equal(DisputeStatus.CLOSED, outcome.Body!.Status);
        Assert.True(outcome.Body.AutoCheckResult.Resolved);

        Assert.Single(_outbox.Published.OfType<DisputeAutoResolvedEvent>());
    }

    /// <summary>
    /// 03 §6.2 Sonuç B — both inventories read, neither moved.
    /// </summary>
    [Fact]
    public async Task Open_Delivery_NoMovement_StaysOpen_AsNotSentYet()
    {
        var tx = await CreateTransactionAsync(
            TransactionStatus.PAYMENT_RECEIVED,
            baselineClassCount: 0,
            baselineClassIds: []);

        // Seller still holds the asset, buyer's count still zero.
        _inventory.Result = InventoryLookupResult.Found(
            BuildSnapshot(tx.ItemAssetId, tx.ItemClassId));
        _inventory.ClassBaseline = InventoryClassBaselineResult.Captured([], []);

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.DELIVERY), CancellationToken.None);

        Assert.Equal(DisputeStatus.OPEN, outcome.Body!.Status);
        Assert.False(outcome.Body.AutoCheckResult.Resolved);
        Assert.True(outcome.Body.AutoCheckResult.CanEscalate);
        Assert.Equal(
            DisputeAutoCheckMessages.Localize(DisputeAutoCheckMessages.DeliveryNotSent, "en"),
            outcome.Body.AutoCheckResult.Message);
    }

    /// <summary>
    /// <b>The launch-gate deadlock regression</b> (T127 validation finding B5).
    /// The gate is closed by default (DEPLOY_RUNBOOK §H), so inventory evidence
    /// accumulates without releasing money. Before T130 the checker read those
    /// flags and closed the dispute as delivered with
    /// <c>CanEscalate = false</c> — leaving the buyer's funds with no exit: the
    /// automatic route gated, the manual route shut.
    /// </summary>
    [Fact]
    public async Task Open_Delivery_InventoryEvidence_LaunchGateClosed_StaysOpenAndEscalatable()
    {
        var tx = await CreateTransactionAsync(
            TransactionStatus.PAYMENT_RECEIVED,
            deliveryEvidence: DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA);

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.DELIVERY), CancellationToken.None);

        Assert.Equal(DisputeStatus.OPEN, outcome.Body!.Status);
        Assert.False(outcome.Body.AutoCheckResult.Resolved);
        Assert.True(outcome.Body.AutoCheckResult.CanEscalate);
        Assert.Equal(
            DisputeAutoCheckMessages.Localize(
                DisputeAutoCheckMessages.DeliveryEvidenceUnderReview, "en"),
            outcome.Body.AutoCheckResult.Message);

        // And the money stayed exactly where it was: no transition, and above
        // all no DeliveryVerifiedAt — the field that actually holds the gate shut.
        var refreshed = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, refreshed.Status);
        Assert.Null(refreshed.DeliveryVerifiedAt);
        Assert.Null(refreshed.PayoutEligibleAt);
    }

    /// <summary>
    /// 03 §6.2 Sonuç A with the gate open — "İşlem ITEM_DELIVERED durumuna
    /// geçer, dispute anında kapanır". The transition is the round's, not the
    /// dispute service's, and both commit in one SaveChanges.
    /// </summary>
    [Fact]
    public async Task Open_Delivery_InventoryEvidence_LaunchGateOpen_DeliversAndCloses()
    {
        await OpenLaunchGateAsync();

        var tx = await CreateTransactionAsync(
            TransactionStatus.PAYMENT_RECEIVED,
            deliveryEvidence: DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA);

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.DELIVERY), CancellationToken.None);

        Assert.Equal(DisputeStatus.CLOSED, outcome.Body!.Status);
        Assert.True(outcome.Body.AutoCheckResult.Resolved);
        Assert.Single(_outbox.Published.OfType<DisputeAutoResolvedEvent>());

        var refreshed = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, refreshed.Status);
        Assert.NotNull(refreshed.DeliveryVerifiedAt);
        // T129 — the settlement window opened with the delivery, not after it.
        Assert.NotNull(refreshed.PayoutEligibleAt);
    }

    /// <summary>
    /// 03 §6.2 Sonuç C — "Otomatik olarak admin'e yükseltilir (kullanıcı
    /// aksiyonu beklenmez)", both parties notified. Before T130 this left the
    /// dispute OPEN and asked the buyer to escalate it themselves.
    /// </summary>
    [Fact]
    public async Task Open_Delivery_MisdeliverySignature_AutoEscalates_AndNotifiesBothParties()
    {
        var tx = await CreateTransactionAsync(
            TransactionStatus.PAYMENT_RECEIVED,
            baselineClassCount: 0,
            baselineClassIds: []);

        // Seller's asset is gone AND the buyer's side was genuinely read — the
        // qualifier that separates a finding from an unreadable inventory.
        _inventory.Result = InventoryLookupResult.NotFound;
        _inventory.ClassBaseline = InventoryClassBaselineResult.Captured([], []);

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.DELIVERY), CancellationToken.None);

        Assert.Equal(DisputeStatus.ESCALATED, outcome.Body!.Status);
        Assert.False(outcome.Body.AutoCheckResult.Resolved);

        var evt = Assert.Single(_outbox.Published.OfType<DisputeEscalatedEvent>());
        Assert.True(evt.AutoEscalated);
        Assert.Equal(_seller.Id, evt.SellerId);
        Assert.Equal(_buyer.Id, evt.BuyerId);
    }

    /// <summary>
    /// 03 §6.2 Sonuç D. A bare <c>SELLER_ASSET_GONE</c> flag with an unreadable
    /// buyer side is NOT the misdelivery signature — the engine qualifies it
    /// with <c>buyerSideKnown</c>, and 08 §2.3 forbids reading "I could not
    /// look" as a finding against a seller.
    /// </summary>
    [Fact]
    public async Task Open_Delivery_UnreadableBuyerInventory_StaysOpen_WithoutBlamingTheSeller()
    {
        var tx = await CreateTransactionAsync(
            TransactionStatus.PAYMENT_RECEIVED,
            deliveryEvidence: DeliveryEvidence.SELLER_ASSET_GONE);

        _inventory.Result = InventoryLookupResult.NotFound;
        _inventory.ClassBaseline = InventoryClassBaselineResult.Private;

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.DELIVERY), CancellationToken.None);

        Assert.Equal(DisputeStatus.OPEN, outcome.Body!.Status);
        Assert.True(outcome.Body.AutoCheckResult.CanEscalate);
        Assert.Equal(
            DisputeAutoCheckMessages.Localize(
                DisputeAutoCheckMessages.DeliveryInventoryUnreadable, "en"),
            outcome.Body.AutoCheckResult.Message);
        Assert.Empty(_outbox.Published.OfType<DisputeEscalatedEvent>());
    }

    // ---------- Open ▸ WRONG_ITEM ----------

    /// <summary>
    /// No 06 §3.5 fingerprint means the buyer's inventory was unreadable when
    /// the baseline was due. The comparison has no reference point, and diffing
    /// against an empty set would read every item the buyer owns as an arrival.
    /// </summary>
    [Fact]
    public async Task Open_WrongItem_NoBaselineFingerprint_StaysOpen()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        var sut = BuildSut();

        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);

        Assert.Equal(DisputeStatus.OPEN, outcome.Body!.Status);
        Assert.False(outcome.Body.AutoCheckResult.Resolved);
        Assert.Equal(
            DisputeAutoCheckMessages.Localize(
                DisputeAutoCheckMessages.WrongItemInventoryUnreadable, "en"),
            outcome.Body.AutoCheckResult.Message);
    }

    /// <summary>03 §6.3 Sonuç A — the expected class count rose.</summary>
    [Fact]
    public async Task Open_WrongItem_ExpectedItemArrived_AutoResolves()
    {
        var tx = await CreateTransactionAsync(
            TransactionStatus.ITEM_DELIVERED,
            baselineClassCount: 0,
            baselineClassIds: ["CLASS-OTHER"]);

        _inventory.Fingerprint = InventoryFingerprintResult.Captured(
        [
            new InventoryFingerprintEntry("other-1", "CLASS-OTHER", null, "Glock-18 | Sand Dune"),
            new InventoryFingerprintEntry("arrived-1", tx.ItemClassId, null, tx.ItemName),
        ]);

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);

        Assert.Equal(DisputeStatus.CLOSED, outcome.Body!.Status);
        Assert.Single(_outbox.Published.OfType<DisputeAutoResolvedEvent>());
    }

    /// <summary>
    /// <b>The acceptance criterion this task exists for.</b> 03 §6.3 Sonuç B —
    /// a different class arrived, so the name is recorded and the dispute
    /// auto-escalates. Before T130 this branch was unreachable: the only asset
    /// id the platform ever recorded came out of a class-scoped diff of the
    /// transaction's own class, so the comparison could only ever match.
    /// </summary>
    [Fact]
    public async Task Open_WrongItem_DifferentClassArrived_AutoEscalates_AndRecordsItsName()
    {
        var tx = await CreateTransactionAsync(
            TransactionStatus.PAYMENT_RECEIVED,
            baselineClassCount: 0,
            baselineClassIds: ["CLASS-OTHER"]);

        _inventory.Fingerprint = InventoryFingerprintResult.Captured(
        [
            new InventoryFingerprintEntry("other-1", "CLASS-OTHER", null, "Glock-18 | Sand Dune"),
            new InventoryFingerprintEntry(
                "wrong-1", "CLASS-AWP-ASIIMOV", null, "AWP | Asiimov (Field-Tested)"),
        ]);

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);

        Assert.Equal(DisputeStatus.ESCALATED, outcome.Body!.Status);

        var evt = Assert.Single(_outbox.Published.OfType<DisputeEscalatedEvent>());
        Assert.True(evt.AutoEscalated);
        Assert.Equal(_seller.Id, evt.SellerId);
        Assert.Equal(_buyer.Id, evt.BuyerId);

        // 02 §10.1 — the admin must not have to make the comparison by hand.
        var dispute = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == outcome.Body.Id);
        Assert.Equal("AWP | Asiimov (Field-Tested)", dispute.DeliveredItemName);

        var refreshedTx = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.True(refreshedTx.HasActiveDispute);
        // A class mismatch is an admin question, never a state transition.
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, refreshedTx.Status);
    }

    /// <summary>
    /// Several classes can arrive between the baseline and the dispute — the
    /// buyer trades on their own account too. The escalation is unconditional,
    /// but the name is not: 06 §8.4's rule is that ambiguity resolves to null
    /// rather than to a guess, and naming the wrong item in an admin's evidence
    /// field is worse than naming none.
    /// </summary>
    [Fact]
    public async Task Open_WrongItem_SeveralClassesArrived_Escalates_WithoutNamingOne()
    {
        var tx = await CreateTransactionAsync(
            TransactionStatus.PAYMENT_RECEIVED,
            baselineClassCount: 0,
            baselineClassIds: []);

        _inventory.Fingerprint = InventoryFingerprintResult.Captured(
        [
            new InventoryFingerprintEntry("a", "CLASS-A", null, "AWP | Asiimov"),
            new InventoryFingerprintEntry("b", "CLASS-B", null, "M4A4 | Howl"),
        ]);

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);

        Assert.Equal(DisputeStatus.ESCALATED, outcome.Body!.Status);

        var dispute = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == outcome.Body.Id);
        Assert.Null(dispute.DeliveredItemName);
    }

    /// <summary>
    /// 03 §6.3 Sonuç C — nothing new arrived at all. "Bu bir yanlış item değil,
    /// teslim edilmeme vakasıdır": the buyer is pointed at the delivery flow and
    /// keeps the escalation route.
    /// </summary>
    [Fact]
    public async Task Open_WrongItem_NothingArrived_StaysOpen_AsADeliveryCase()
    {
        var tx = await CreateTransactionAsync(
            TransactionStatus.PAYMENT_RECEIVED,
            baselineClassCount: 0,
            baselineClassIds: ["CLASS-OTHER"]);

        _inventory.Fingerprint = InventoryFingerprintResult.Captured(
            [new InventoryFingerprintEntry("other-1", "CLASS-OTHER", null, "Glock-18 | Sand Dune")]);

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);

        Assert.Equal(DisputeStatus.OPEN, outcome.Body!.Status);
        Assert.True(outcome.Body.AutoCheckResult.CanEscalate);
        Assert.Equal(
            DisputeAutoCheckMessages.Localize(DisputeAutoCheckMessages.WrongItemNoDelivery, "en"),
            outcome.Body.AutoCheckResult.Message);
        Assert.Empty(_outbox.Published.OfType<DisputeEscalatedEvent>());
    }

    [Theory]
    [InlineData(InventoryVisibility.Private)]     // hidden profile
    [InlineData(InventoryVisibility.Unavailable)] // Steam / sidecar down
    public async Task Open_WrongItem_UnreadableInventory_StaysOpen(InventoryVisibility visibility)
    {
        // WP6 harden, restated on the T130 mechanism — an unreadable inventory
        // is not an empty one. Diffing it would manufacture the opposite
        // finding: every baseline class would look gone and nothing arrived.
        // The checker must fail closed rather than auto-escalate off missing
        // data (08 §2.3).
        var tx = await CreateTransactionAsync(
            TransactionStatus.ITEM_DELIVERED,
            baselineClassCount: 0,
            baselineClassIds: ["CLASS-OTHER"]);

        _inventory.Fingerprint = visibility == InventoryVisibility.Private
            ? InventoryFingerprintResult.Private
            : InventoryFingerprintResult.Unavailable;

        var sut = BuildSut();
        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);

        Assert.Equal(DisputeStatus.OPEN, outcome.Body!.Status);
        Assert.False(outcome.Body.AutoCheckResult.Resolved);
        Assert.True(outcome.Body.AutoCheckResult.CanEscalate);
        Assert.Empty(_outbox.Published.OfType<DisputeEscalatedEvent>());
        Assert.Empty(_outbox.Published.OfType<DisputeAutoResolvedEvent>());

        var refreshedTx = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.True(refreshedTx.HasActiveDispute);
    }

    /// <summary>
    /// 02 §10.1 requires the dispute path to re-run the rules "taze olarak": a
    /// cached read can still be missing an item that arrived a minute ago.
    /// </summary>
    [Fact]
    public async Task Open_WrongItem_ReadsTheInventoryFresh()
    {
        var tx = await CreateTransactionAsync(
            TransactionStatus.ITEM_DELIVERED,
            baselineClassCount: 0,
            baselineClassIds: []);
        _inventory.Fingerprint = InventoryFingerprintResult.Captured([]);

        var sut = BuildSut();
        await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);

        Assert.Equal(
            InventoryReadFreshness.Fresh,
            Assert.Single(_inventory.FingerprintReadFreshness));
    }

    // ---------- Open ▸ guards ----------

    [Fact]
    public async Task Open_NotBuyer_Returns_NotBuyer()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED);
        var sut = BuildSut();

        var outcome = await sut.OpenAsync(_otherUser.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.NotBuyer, outcome.Status);
        Assert.Equal(DisputeErrorCodes.NotBuyer, outcome.ErrorCode);
    }

    [Fact]
    public async Task Open_TransactionNotFound_Returns_NotFound()
    {
        var sut = BuildSut();

        var outcome = await sut.OpenAsync(_buyer.Id, Guid.NewGuid(),
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.NotFound, outcome.Status);
        Assert.Equal(DisputeErrorCodes.TransactionNotFound, outcome.ErrorCode);
    }

    [Fact]
    public async Task Open_StateNotAllowedForType_Returns_InvalidStateTransition()
    {
        // PAYMENT is only openable in SELLER_CONFIRMED / PAYMENT_RECEIVED per
        // the canonical DisputeEligibility matrix (02 §10.1); by ITEM_DELIVERED
        // the payment question is settled.
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        var sut = BuildSut();

        var outcome = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.InvalidStateTransition, outcome.Status);
        Assert.Equal(DisputeErrorCodes.InvalidStateTransition, outcome.ErrorCode);
    }

    [Fact]
    public async Task Open_DuplicateType_AfterClose_Returns_DuplicateDispute()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED);
        // Force a confirmed payment so the first open auto-resolves to CLOSED.
        await SeedConfirmedBuyerPaymentAsync(tx, "abc123def456789012345678");

        var sut = BuildSut();
        var first = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(OpenDisputeStatus.Opened, first.Status);

        var second = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        Assert.Equal(OpenDisputeStatus.DuplicateDispute, second.Status);
        Assert.Equal(DisputeErrorCodes.DuplicateDispute, second.ErrorCode);
    }

    [Fact]
    public async Task Open_DifferentTypes_Concurrently_Allowed()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);

        var sut = BuildSut();
        var delivery = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.DELIVERY), CancellationToken.None);
        Assert.Equal(OpenDisputeStatus.Opened, delivery.Status);

        var wrongItem = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);
        Assert.Equal(OpenDisputeStatus.Opened, wrongItem.Status);

        var count = await Context.Set<Dispute>().CountAsync(d => d.TransactionId == tx.Id);
        Assert.Equal(2, count);
    }

    // ---------- SubmitTxHash ----------

    [Fact]
    public async Task SubmitTxHash_MatchingHash_Resolves_AndClearsActiveDisputeFlag()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        // Sidecar (T71) writes a CONFIRMED row with the hash the buyer is
        // about to submit (canonical lowercase per T71 normalization contract).
        const string hash = "ABC123DEF456789012345678";
        await SeedConfirmedBuyerPaymentAsync(tx, hash.ToLowerInvariant());

        var outcome = await sut.SubmitTxHashAsync(_buyer.Id, tx.Id, open.Body.Id,
            new SubmitTxHashRequest(hash), CancellationToken.None);

        Assert.Equal(SubmitTxHashStatus.Processed, outcome.Status);
        Assert.True(outcome.Body!.CheckResult.Resolved);

        var refreshedDispute = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == open.Body.Id);
        Assert.Equal(DisputeStatus.CLOSED, refreshedDispute.Status);
        Assert.NotNull(refreshedDispute.ResolvedAt);

        var refreshedTx = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.False(refreshedTx.HasActiveDispute);

        Assert.Single(_outbox.Published.OfType<DisputeAutoResolvedEvent>());
    }

    [Fact]
    public async Task SubmitTxHash_NoMatch_StaysOpen()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        var outcome = await sut.SubmitTxHashAsync(_buyer.Id, tx.Id, open.Body.Id,
            new SubmitTxHashRequest("0123456789abcdef0123"), CancellationToken.None);

        Assert.Equal(SubmitTxHashStatus.Processed, outcome.Status);
        Assert.False(outcome.Body!.CheckResult.Resolved);

        var refreshedDispute = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == open.Body.Id);
        Assert.Equal(DisputeStatus.OPEN, refreshedDispute.Status);
    }

    [Fact]
    public async Task SubmitTxHash_NonPaymentDispute_Returns_NotPaymentDispute()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);

        var sut = BuildSut();
        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.DELIVERY), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        var outcome = await sut.SubmitTxHashAsync(_buyer.Id, tx.Id, open.Body.Id,
            new SubmitTxHashRequest("0123456789abcdef0123"), CancellationToken.None);

        Assert.Equal(SubmitTxHashStatus.NotPaymentDispute, outcome.Status);
        Assert.Equal(DisputeErrorCodes.NotPaymentDispute, outcome.ErrorCode);
    }

    [Fact]
    public async Task SubmitTxHash_DisputeClosed_Returns_DisputeClosed()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED);
        await SeedConfirmedBuyerPaymentAsync(tx, "abc123def456789012345678");

        var sut = BuildSut();
        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.CLOSED, open.Body!.Status);

        var outcome = await sut.SubmitTxHashAsync(_buyer.Id, tx.Id, open.Body.Id,
            new SubmitTxHashRequest("0123456789abcdef0123"), CancellationToken.None);

        Assert.Equal(SubmitTxHashStatus.DisputeClosed, outcome.Status);
        Assert.Equal(DisputeErrorCodes.DisputeClosed, outcome.ErrorCode);
    }

    [Fact]
    public async Task SubmitTxHash_BadHashLength_Returns_ValidationError()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        var outcome = await sut.SubmitTxHashAsync(_buyer.Id, tx.Id, open.Body.Id,
            new SubmitTxHashRequest("short"), CancellationToken.None);

        Assert.Equal(SubmitTxHashStatus.ValidationFailed, outcome.Status);
        Assert.Equal(DisputeErrorCodes.ValidationError, outcome.ErrorCode);
    }

    [Fact]
    public async Task SubmitTxHash_NonBuyer_Returns_NotBuyer()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        var outcome = await sut.SubmitTxHashAsync(_otherUser.Id, tx.Id, open.Body.Id,
            new SubmitTxHashRequest("0123456789abcdef0123"), CancellationToken.None);

        Assert.Equal(SubmitTxHashStatus.NotBuyer, outcome.Status);
    }

    // ---------- Escalate ----------

    [Fact]
    public async Task Escalate_OpenDispute_PromotesToEscalated_AndEmitsEvent()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        var outcome = await sut.EscalateAsync(_buyer.Id, tx.Id, open.Body.Id,
            new EscalateDisputeRequest("Ödemeyi gönderdim ama sistem hala görmüyor"),
            CancellationToken.None);

        Assert.Equal(EscalateDisputeStatus.Escalated, outcome.Status);
        Assert.Equal(DisputeStatus.ESCALATED, outcome.Body!.Status);
        // WP17 — escalate response is localized to the buyer's locale ("en" here).
        Assert.Equal("Your dispute has been forwarded to the admin team", outcome.Body.Message);

        var refreshedDispute = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == open.Body.Id);
        Assert.Equal(DisputeStatus.ESCALATED, refreshedDispute.Status);
        Assert.Equal("Ödemeyi gönderdim ama sistem hala görmüyor", refreshedDispute.UserDescription);

        var refreshedTx = await Context.Set<Transaction>().AsNoTracking()
            .FirstAsync(t => t.Id == tx.Id);
        Assert.True(refreshedTx.HasActiveDispute);

        var evt = Assert.Single(_outbox.Published.OfType<DisputeEscalatedEvent>());
        Assert.False(evt.AutoEscalated);
        Assert.Equal(_buyer.Id, evt.BuyerId);
    }

    [Fact]
    public async Task Escalate_AlreadyEscalated_Returns_AlreadyEscalated()
    {
        // T130 — the auto-escalation now comes from the inventory diff rather
        // than from a DeliveredBuyerAssetId lookup: a foreign class arrived that
        // was not in the 06 §3.5 baseline fingerprint.
        var tx = await CreateTransactionAsync(
            TransactionStatus.ITEM_DELIVERED,
            baselineClassCount: 0,
            baselineClassIds: []);
        _inventory.Fingerprint = InventoryFingerprintResult.Captured(
            [new InventoryFingerprintEntry("wrong-3", "OTHER-CLASS", null, "AWP | Asiimov")]);

        var sut = BuildSut();
        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.WRONG_ITEM), CancellationToken.None);
        Assert.Equal(DisputeStatus.ESCALATED, open.Body!.Status);

        var outcome = await sut.EscalateAsync(_buyer.Id, tx.Id, open.Body.Id,
            new EscalateDisputeRequest("Bekliyorum çözüm bekliyorum"),
            CancellationToken.None);

        Assert.Equal(EscalateDisputeStatus.AlreadyEscalated, outcome.Status);
        Assert.Equal(DisputeErrorCodes.AlreadyEscalated, outcome.ErrorCode);
    }

    [Fact]
    public async Task Escalate_ClosedDispute_Returns_DisputeClosed()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED);
        await SeedConfirmedBuyerPaymentAsync(tx, "abc123def456789012345678");

        var sut = BuildSut();
        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.CLOSED, open.Body!.Status);

        var outcome = await sut.EscalateAsync(_buyer.Id, tx.Id, open.Body.Id,
            new EscalateDisputeRequest("Yine de admin baksın lütfen"),
            CancellationToken.None);

        Assert.Equal(EscalateDisputeStatus.DisputeClosed, outcome.Status);
    }

    [Fact]
    public async Task Escalate_DetailTooShort_Returns_ValidationFailed()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);
        Assert.Equal(DisputeStatus.OPEN, open.Body!.Status);

        var outcome = await sut.EscalateAsync(_buyer.Id, tx.Id, open.Body.Id,
            new EscalateDisputeRequest("kısa"),
            CancellationToken.None);

        Assert.Equal(EscalateDisputeStatus.ValidationFailed, outcome.Status);
    }

    [Fact]
    public async Task Escalate_NotBuyer_Returns_NotBuyer()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED);
        var sut = BuildSut();

        var open = await sut.OpenAsync(_buyer.Id, tx.Id,
            new OpenDisputeRequest(DisputeType.PAYMENT), CancellationToken.None);

        var outcome = await sut.EscalateAsync(_otherUser.Id, tx.Id, open.Body!.Id,
            new EscalateDisputeRequest("Ben başka biriyim ama itiraz ediyorum"),
            CancellationToken.None);

        Assert.Equal(EscalateDisputeStatus.NotBuyer, outcome.Status);
    }

    [Fact]
    public async Task Escalate_DisputeNotFound_Returns_NotFound()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED);
        var sut = BuildSut();

        var outcome = await sut.EscalateAsync(_buyer.Id, tx.Id, Guid.NewGuid(),
            new EscalateDisputeRequest("Var olmayan bir dispute"),
            CancellationToken.None);

        Assert.Equal(EscalateDisputeStatus.NotFound, outcome.Status);
        Assert.Equal(DisputeErrorCodes.DisputeNotFound, outcome.ErrorCode);
    }

    // ---------- Test helpers ----------

    private DisputeService BuildSut()
    {
        var paymentChecker = new PaymentDisputeAutoChecker(Context);

        // T130 — the REAL round, not a stub. The dispute path is where the
        // 02 §9.2 rules are re-run "taze olarak", and the launch-gate deadlock
        // this task closes only exists as a composition of the engine, the gate
        // and the checker's mapping. Stubbing the round here would test the
        // mapping twice and the composition never.
        var deliveryChecker = new DeliveryDisputeAutoChecker(
            new DeliveryDisputeRound(
                db: Context,
                verification: new DeliveryVerificationService(
                    Context, _inventory,
                    NullLogger<DeliveryVerificationService>.Instance, _clock),
                settlementSettings: new SettlementSettingsProvider(Context),
                outbox: _outbox,
                logger: NullLogger<DeliveryDisputeRound>.Instance,
                clock: _clock));

        var wrongItemChecker = new WrongItemDisputeAutoChecker(
            Context, _inventory, NullLogger<WrongItemDisputeAutoChecker>.Instance);

        return new DisputeService(
            db: Context,
            outbox: _outbox,
            paymentChecker: paymentChecker,
            deliveryChecker: deliveryChecker,
            wrongItemChecker: wrongItemChecker,
            clock: _clock);
    }

    /// <summary>
    /// Inserts a CONFIRMED BUYER_PAYMENT row + the prerequisite PaymentAddress
    /// row that <c>CK_BlockchainTransactions_Type_BuyerPayment</c> requires
    /// (PaymentAddressId NOT NULL) and that <c>CK_BlockchainTransactions_Status_Confirmed</c>
    /// requires (ConfirmationCount >= 20, ConfirmedAt NOT NULL).
    /// Mirrors the T56 lesson learned in <c>FraudFlagServiceTests.InsertBuyerPaymentAsync</c>.
    /// </summary>
    private async Task SeedConfirmedBuyerPaymentAsync(Transaction tx, string txHash)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        var paymentAddress = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Address = "TBuyerDepositAddress0000000000000",
            HdWalletIndex = 1,
            ExpectedAmount = tx.TotalAmount,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Context.Set<PaymentAddress>().Add(paymentAddress);

        Context.Set<BlockchainTransaction>().Add(new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = paymentAddress.Id,
            Type = BlockchainTransactionType.BUYER_PAYMENT,
            FromAddress = "TBuyer",
            ToAddress = paymentAddress.Address,
            Amount = tx.TotalAmount,
            Token = StablecoinType.USDT,
            Status = BlockchainTransactionStatus.CONFIRMED,
            ConfirmationCount = 20,
            CreatedAt = now,
            ConfirmedAt = now,
            TxHash = txHash,
        });

        await Context.SaveChangesAsync();
    }

    /// <summary>
    /// T130 — open the launch gate (DEPLOY_RUNBOOK §H). Closed by default, and
    /// deliberately so: the gate is the reason a delivery dispute must not close
    /// itself as "delivered" on inventory evidence alone.
    /// </summary>
    private async Task OpenLaunchGateAsync()
    {
        // The row is seeded (unconfigured) by the schema seed, and
        // UQ_SystemSettings_Key is unfiltered — so this updates rather than
        // inserts, exactly as the admin UI does (DEPLOY_RUNBOOK §H.3 step 4).
        var existing = await Context.Set<SystemSetting>()
            .FirstOrDefaultAsync(s => s.Key == DeliveryVerificationService.AutoReleaseSettingKey);

        if (existing is null)
        {
            Context.Set<SystemSetting>().Add(new SystemSetting
            {
                Id = Guid.NewGuid(),
                Key = DeliveryVerificationService.AutoReleaseSettingKey,
                Value = "true",
                IsConfigured = true,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
                UpdatedAt = _clock.GetUtcNow().UtcDateTime,
            });
        }
        else
        {
            existing.Value = "true";
            existing.IsConfigured = true;
            existing.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        }

        await Context.SaveChangesAsync();
    }

    private async Task<Transaction> CreateTransactionAsync(
        TransactionStatus status,
        string? deliveredAssetId = null,
        DeliveryEvidence deliveryEvidence = DeliveryEvidence.NONE,
        int? baselineClassCount = null,
        IReadOnlyList<string>? baselineClassIds = null)
    {
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = BuyerSteam,
            ItemAssetId = "asset-1",
            ItemClassId = "CLASS-AK47-REDLINE",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 50.00m,
            CommissionRate = 0.03m,
            CommissionAmount = 1.50m,
            TotalAmount = 51.50m,
            SellerPayoutAddress = SellerWallet,
            PaymentTimeoutMinutes = 60,

            // 06 §3.5 — the fields ACCEPTED and later states require. Set on
            // every fixture because T130's delivery arm fires DeliverItem, whose
            // guard walks the whole matrix back to ACCEPTED; without them the
            // transition is refused and the round holds instead of delivering.
            BuyerRefundAddress = "TBuyerRefundAddress00000000000000",
            BuyerTradeUrl = "https://steamcommunity.com/tradeoffer/new/?partner=1&token=abc",
            SellerReadyConfirmedAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(-5),
            DeliveredBuyerAssetId = deliveredAssetId,
            DeliveryEvidence = deliveryEvidence,

            // 06 §3.5 — the four baseline columns live and die together: a
            // fingerprint without a CapturedAt is not a baseline, it is a
            // half-written row (T130 reads CapturedAt as the "is there one at
            // all" signal, exactly as the evidence engine does).
            BuyerBaselineClassCount = baselineClassCount,
            BuyerBaselineClassIds = baselineClassIds is null
                ? null
                : JsonSerializer.Serialize(baselineClassIds),
            BuyerBaselineCapturedAt = baselineClassIds is null && baselineClassCount is null
                ? null
                : _clock.GetUtcNow().UtcDateTime.AddMinutes(-20),

            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            UpdatedAt = _clock.GetUtcNow().UtcDateTime,
            AcceptedAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(-10),
        };
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx;
    }

    private static InventoryItemSnapshot BuildSnapshot(string assetId, string classId) =>
        new(
            AssetId: assetId,
            ClassId: classId,
            InstanceId: null,
            Name: "Snapshot",
            MarketHashName: "Snapshot",
            IconUrl: null,
            Exterior: null,
            Type: null,
            InspectLink: null,
            IsTradeable: true);

    private sealed class RecordingOutboxService : IOutboxService
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInventoryReader : ISteamInventoryReader
    {
        /// <summary>
        /// T121 — the double now carries the full 08 §2.3 outcome. It used to
        /// expose a nullable snapshot, which could not tell "inventory read,
        /// asset absent" apart from "inventory unreadable".
        /// </summary>
        public InventoryLookupResult Result { get; set; } = InventoryLookupResult.NotFound;

        public InventoryItemSnapshot? Snapshot
        {
            set => Result = value is null
                ? InventoryLookupResult.NotFound
                : InventoryLookupResult.Found(value);
        }

        public Task<InventoryLookupResult> GetItemAsync(
            string steamId64,
            string itemAssetId,
            InventoryReadFreshness freshness,
            CancellationToken cancellationToken)
            => Task.FromResult(Result);

        /// <summary>
        /// T123 — unused by the dispute suites; answers Unavailable rather than
        /// an empty baseline so an accidental caller gets "unknown", never a
        /// fabricated "the buyer owns none of this skin".
        /// </summary>
        /// <summary>
        /// T130 — the buyer's side of a delivery round. Defaults to Unavailable
        /// so a suite that does not set it gets "unknown" rather than a
        /// fabricated "the buyer owns none of this skin".
        /// </summary>
        public InventoryClassBaselineResult ClassBaseline { get; set; } =
            InventoryClassBaselineResult.Unavailable;

        public Task<InventoryClassBaselineResult> CaptureClassBaselineAsync(
            string steamId64,
            string classId,
            string? instanceId,
            InventoryReadFreshness freshness,
            CancellationToken cancellationToken)
            => Task.FromResult(ClassBaseline);

        /// <summary>
        /// T130 — the buyer's inventory as the wrong-item comparison sees it.
        /// Defaults to Unavailable so a suite that does not set it gets
        /// "unknown" rather than "the buyer's inventory is empty".
        /// </summary>
        public InventoryFingerprintResult Fingerprint { get; set; } =
            InventoryFingerprintResult.Unavailable;

        /// <summary>T130 — freshness of each fingerprint read (02 §10.1 "taze").</summary>
        public List<InventoryReadFreshness> FingerprintReadFreshness { get; } = [];

        public Task<InventoryFingerprintResult> CaptureInventoryFingerprintAsync(
            string steamId64,
            InventoryReadFreshness freshness,
            CancellationToken cancellationToken)
        {
            FingerprintReadFreshness.Add(freshness);
            return Task.FromResult(Fingerprint);
        }
    }
}

