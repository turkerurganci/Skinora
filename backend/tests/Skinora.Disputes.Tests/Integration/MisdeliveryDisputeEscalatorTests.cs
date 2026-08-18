using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Disputes.Application.AutoCheckers;
using Skinora.Disputes.Application.Disputes;
using Skinora.Disputes.Domain.Entities;
using Skinora.Disputes.Infrastructure.Persistence;
using Skinora.Shared.Domain;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Application.Delivery;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Disputes.Tests.Integration;

/// <summary>
/// T127 — <see cref="MisdeliveryDisputeEscalator"/>, the Disputes-side half of
/// the delivery timeout's escalation (02 §9.2, §10.1).
/// </summary>
/// <remarks>
/// The port exists because the module dependency runs Disputes → Transactions,
/// so the timeout round cannot raise a Dispute itself. What it must get right
/// is the one rule the round cannot see: 02 §10.2 allows a single dispute per
/// (transaction, type), enforced by an UNFILTERED unique index, while the
/// scanner may reach the same transaction on every pass.
/// </remarks>
public class MisdeliveryDisputeEscalatorTests : IntegrationTestBase
{
    static MisdeliveryDisputeEscalatorTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
        DisputesModuleDbRegistration.RegisterDisputesModule();
    }

    private const string SellerSteam = "76561198000000401";
    private const string BuyerSteam = "76561198000000402";
    private const string SellerWallet = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL";

    private FakeTimeProvider _clock = null!;
    private RecordingOutboxService _outbox = null!;
    private User _seller = null!;
    private User _buyer = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero));
        _outbox = new RecordingOutboxService();

        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteam,
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = SellerWallet,
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = BuyerSteam,
            SteamDisplayName = "Buyer",
            PreferredLanguage = "tr",
        };
        context.Set<User>().AddRange(_seller, _buyer);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// The first escalation. Opened by SYSTEM rather than by the buyer — the
    /// buyer may not even know anything is wrong, since from their side nothing
    /// arrived and the transaction merely looks slow (02 §10.1).
    /// </summary>
    [Fact]
    public async Task Opens_A_System_Escalated_Delivery_Dispute()
    {
        var tx = await CreateTransactionAsync();

        var outcome = await EscalateAsync(tx);

        Assert.Equal(MisdeliveryEscalationOutcome.Opened, outcome);
        await Context.SaveChangesAsync();

        var dispute = await Context.Set<Dispute>().AsNoTracking()
            .SingleAsync(d => d.TransactionId == tx.Id);
        Assert.Equal(DisputeType.DELIVERY, dispute.Type);
        Assert.Equal(DisputeStatus.ESCALATED, dispute.Status);
        Assert.Equal(SeedConstants.SystemUserId, dispute.OpenedByUserId);
        // WP17 — stored in the buyer's locale, like the buyer-opened path.
        Assert.Equal(
            DisputeAutoCheckMessages.Localize(
                DisputeAutoCheckMessages.DeliveryAssetGoneNotArrived, "tr"),
            dispute.SystemCheckResult);
        // Not resolved: an escalation is the START of admin review (06 §3.11).
        Assert.Null(dispute.ResolvedAt);

        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == tx.Id);
        Assert.True(persisted.HasActiveDispute);

        // 03 §6.3 step 5 — both parties are told the transaction is under
        // review, which is what AutoEscalated selects in the consumer.
        var evt = Assert.Single(_outbox.Published.OfType<DisputeEscalatedEvent>());
        Assert.True(evt.AutoEscalated);
        Assert.Equal(DisputeType.DELIVERY, evt.Type);
        Assert.Equal(_seller.Id, evt.SellerId);
        Assert.Equal(_buyer.Id, evt.BuyerId);
        Assert.Null(evt.Detail);
    }

    /// <summary>
    /// The buyer got there first and the auto-checker left the dispute OPEN so
    /// they could escalate it themselves (03 §6.2). The platform has now
    /// established the signature on its own, so it does not wait for them to
    /// press a button.
    /// </summary>
    [Fact]
    public async Task Promotes_A_Buyer_Opened_Dispute_Instead_Of_Inserting_A_Second()
    {
        var tx = await CreateTransactionAsync();
        var existing = await AddDisputeAsync(tx, DisputeStatus.OPEN);

        var outcome = await EscalateAsync(tx);

        Assert.Equal(MisdeliveryEscalationOutcome.Promoted, outcome);
        await Context.SaveChangesAsync();

        // UQ_Disputes_TransactionId_Type is unfiltered, so a second row is not
        // merely undesirable — it would throw and take the scanner batch down.
        var dispute = await Context.Set<Dispute>().AsNoTracking()
            .SingleAsync(d => d.TransactionId == tx.Id);
        Assert.Equal(existing.Id, dispute.Id);
        Assert.Equal(DisputeStatus.ESCALATED, dispute.Status);
        // The platform's finding is recorded...
        Assert.Equal(
            DisputeAutoCheckMessages.Localize(
                DisputeAutoCheckMessages.DeliveryAssetGoneNotArrived, "tr"),
            dispute.SystemCheckResult);
        // ...and the buyer's own text survives it. The escalation adds a
        // finding, it does not overwrite what they wrote.
        Assert.Equal("Item hiç gelmedi", dispute.UserDescription);

        Assert.Single(_outbox.Published.OfType<DisputeEscalatedEvent>());
    }

    /// <summary>
    /// The scanner reaches a held transaction on every pass, so this is the
    /// common case rather than an edge one: nothing is written and — the point
    /// of the test — both parties are not notified again every interval.
    /// </summary>
    [Fact]
    public async Task Already_Escalated_Is_A_Silent_No_Op()
    {
        var tx = await CreateTransactionAsync();
        await AddDisputeAsync(tx, DisputeStatus.ESCALATED);

        var outcome = await EscalateAsync(tx);

        Assert.Equal(MisdeliveryEscalationOutcome.AlreadyEscalated, outcome);
        Assert.Empty(_outbox.Published);
        await Context.SaveChangesAsync();

        var dispute = await Context.Set<Dispute>().AsNoTracking()
            .SingleAsync(d => d.TransactionId == tx.Id);
        Assert.Equal(DisputeStatus.ESCALATED, dispute.Status);
        // Untouched — a re-assertion is not an update. The fixture leaves this
        // NULL, so a rewritten finding would show up here.
        Assert.Null(dispute.SystemCheckResult);

        // Still flagged: ESCALATED is an active dispute (06 §3.11).
        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .SingleAsync(t => t.Id == tx.Id);
        Assert.True(persisted.HasActiveDispute);
    }

    /// <summary>
    /// Already answered. The unique index forbids a second row and re-opening a
    /// settled decision is not a timeout's call, so the dispute is left alone in
    /// every resolved terminal — but the ANSWER differs by who settled it
    /// (T131 / T127 finding G3).
    /// </summary>
    /// <remarks>
    /// CLOSED is the system's own auto-resolution (06 §2.10): nobody looked, so
    /// the caller must keep holding — cancelling there would still be the silent
    /// cancellation 02 §9.2 forbids. RESOLVED_FOR_* means a human read the case,
    /// which is the one thing that lifts the hold — provided the case they read
    /// INCLUDED the signature, which is why the RESOLVED_FOR_* rows below place
    /// the signature half an hour before the ruling (T131 finding N2).
    /// </remarks>
    [Theory]
    [InlineData(DisputeStatus.CLOSED, MisdeliveryEscalationOutcome.AlreadyResolved)]
    [InlineData(DisputeStatus.RESOLVED_FOR_BUYER, MisdeliveryEscalationOutcome.AlreadyRuledByAdmin)]
    [InlineData(DisputeStatus.RESOLVED_FOR_SELLER, MisdeliveryEscalationOutcome.AlreadyRuledByAdmin)]
    public async Task Resolved_Dispute_Is_Left_Alone(
        DisputeStatus status, MisdeliveryEscalationOutcome expected)
    {
        var tx = await CreateTransactionAsync();
        await AddDisputeAsync(tx, status);

        var outcome = await EscalateAsync(
            tx, signatureFirstObservedAtUtc: _clock.GetUtcNow().UtcDateTime.AddMinutes(-30));

        Assert.Equal(expected, outcome);
        Assert.Empty(_outbox.Published);
        await Context.SaveChangesAsync();

        var dispute = await Context.Set<Dispute>().AsNoTracking()
            .SingleAsync(d => d.TransactionId == tx.Id);
        Assert.Equal(status, dispute.Status);
    }

    /// <summary>
    /// <b>Finding N2 — a ruling made without the signature does not release the
    /// hold; it sends the case back.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The release exists for exactly one reason: a human read the case, so the
    /// cancellation 02 §9.2 forbids being SILENT no longer is. An admin who
    /// ruled before the signature existed read a different case. Releasing on
    /// their ruling cancels the transaction and refunds the buyer — quietly
    /// reversing a seller-favour decision on evidence its author never saw, and
    /// leaving a seller whose item has already left their inventory with neither
    /// the item nor the money.
    /// </para>
    /// <para>
    /// Both terminals are covered because the rule is about the ORDER, not about
    /// which way the ruling went.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(DisputeStatus.RESOLVED_FOR_SELLER)]
    [InlineData(DisputeStatus.RESOLVED_FOR_BUYER)]
    public async Task A_Ruling_That_Predates_The_Signature_Re_Escalates(DisputeStatus ruling)
    {
        var tx = await CreateTransactionAsync();
        var ruledAt = _clock.GetUtcNow().UtcDateTime;
        await AddDisputeAsync(tx, ruling);

        // The signature is established a minute AFTER the ruling — the shape the
        // first-round arm produces, where the round passes its own clock.
        var outcome = await EscalateAsync(
            tx, signatureFirstObservedAtUtc: ruledAt.AddMinutes(1));

        Assert.Equal(MisdeliveryEscalationOutcome.ReEscalatedAfterRuling, outcome);
        await Context.SaveChangesAsync();

        var dispute = await Context.Set<Dispute>().AsNoTracking()
            .SingleAsync(d => d.TransactionId == tx.Id);
        Assert.Equal(DisputeStatus.ESCALATED, dispute.Status);
        // Cleared: the row is not resolved any more, and
        // CK_Disputes_Resolved_ResolvedAt pairs the two. The previous ruling
        // survives in the append-only audit trail (06 §3.20).
        Assert.Null(dispute.ResolvedAt);
        // WP17 — the new finding replaces the old text, in the buyer's locale.
        Assert.Equal(
            DisputeAutoCheckMessages.Localize(
                DisputeAutoCheckMessages.DeliveryAssetGoneNotArrived, "tr"),
            dispute.SystemCheckResult);

        // The admin queue is not enough on its own — both parties are told, the
        // same way the first escalation tells them (03 §6.3 step 5).
        var escalated = Assert.IsType<DisputeEscalatedEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(dispute.Id, escalated.DisputeId);
        Assert.True(escalated.AutoEscalated);

        var tracked = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.True(tracked.HasActiveDispute);
    }

    /// <summary>
    /// A ruling made in the SAME instant the signature was recorded is treated
    /// as one made without it (T131 finding N2).
    /// </summary>
    /// <remarks>
    /// Establishing the signature is what writes it onto the dispute, so an
    /// admin who ruled at that instant cannot have read it. The two errors are
    /// not symmetric — a needless review costs an admin's time, a wrong release
    /// costs a seller their item and their money — so the ambiguous input takes
    /// the conservative arm.
    /// </remarks>
    [Fact]
    public async Task A_Ruling_In_The_Same_Instant_As_The_Signature_Re_Escalates()
    {
        var tx = await CreateTransactionAsync();
        await AddDisputeAsync(tx, DisputeStatus.RESOLVED_FOR_SELLER);

        // AddDisputeAsync stamps ResolvedAt with the same clock the default
        // signature time comes from.
        var outcome = await EscalateAsync(tx);

        Assert.Equal(MisdeliveryEscalationOutcome.ReEscalatedAfterRuling, outcome);
    }

    /// <summary>
    /// CLOSED is never re-escalated, whatever the order of the timestamps
    /// (T131 finding N2).
    /// </summary>
    /// <remarks>
    /// The N2 rule is about a HUMAN having ruled without seeing the evidence.
    /// CLOSED is the system answering its own question (06 §2.10) — there is no
    /// ruling to preserve or to revisit, and the existing arm already refuses to
    /// release the caller's hold, so the money is not stuck on this path.
    /// </remarks>
    [Fact]
    public async Task A_Closed_Dispute_Is_Not_Re_Escalated_By_A_Later_Signature()
    {
        var tx = await CreateTransactionAsync();
        var closedAt = _clock.GetUtcNow().UtcDateTime;
        await AddDisputeAsync(tx, DisputeStatus.CLOSED);

        var outcome = await EscalateAsync(
            tx, signatureFirstObservedAtUtc: closedAt.AddMinutes(1));

        Assert.Equal(MisdeliveryEscalationOutcome.AlreadyResolved, outcome);
        Assert.Empty(_outbox.Published);
        await Context.SaveChangesAsync();

        var dispute = await Context.Set<Dispute>().AsNoTracking()
            .SingleAsync(d => d.TransactionId == tx.Id);
        Assert.Equal(DisputeStatus.CLOSED, dispute.Status);
        Assert.NotNull(dispute.ResolvedAt);
    }

    /// <summary>
    /// The port contract: rows land on the caller's tracked context and nothing
    /// is saved here. The escalation and the evidence capture that justifies it
    /// have to commit together — a capture saying "the item went somewhere else"
    /// with no dispute attached is the silent cancellation 02 §9.2 forbids.
    /// </summary>
    [Fact]
    public async Task Writes_Nothing_Until_The_Caller_Saves()
    {
        var tx = await CreateTransactionAsync();

        await EscalateAsync(tx);

        Context.ChangeTracker.Clear();
        Assert.Empty(await Context.Set<Dispute>().AsNoTracking().ToListAsync());
    }

    // ================= Helpers =================

    /// <summary>
    /// Run the adapter. <paramref name="signatureFirstObservedAtUtc"/> defaults
    /// to "now", which is the round that ESTABLISHES the signature — the case
    /// where every existing ruling necessarily predates it (T131 finding N2).
    /// Tests about a ruling made with the signature on record pass an earlier
    /// time explicitly.
    /// </summary>
    private async Task<MisdeliveryEscalationOutcome> EscalateAsync(
        Transaction tx, DateTime? signatureFirstObservedAtUtc = null)
    {
        var tracked = await Context.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);
        var now = _clock.GetUtcNow().UtcDateTime;
        var sut = new MisdeliveryDisputeEscalator(
            Context, _outbox, NullLogger<MisdeliveryDisputeEscalator>.Instance);
        return await sut.EscalateAsync(
            tracked, now, signatureFirstObservedAtUtc ?? now, CancellationToken.None);
    }

    private async Task<Dispute> AddDisputeAsync(Transaction tx, DisputeStatus status)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var isResolved = status is DisputeStatus.CLOSED
            or DisputeStatus.RESOLVED_FOR_BUYER
            or DisputeStatus.RESOLVED_FOR_SELLER;

        var dispute = new Dispute
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            OpenedByUserId = _buyer.Id,
            Type = DisputeType.DELIVERY,
            Status = status,
            UserDescription = "Item hiç gelmedi",
            // CK_Disputes_Closed — a closed dispute carries its resolution time.
            ResolvedAt = isResolved ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Context.Set<Dispute>().Add(dispute);

        var tracked = await Context.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);
        tracked.HasActiveDispute = status is DisputeStatus.OPEN or DisputeStatus.ESCALATED;
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return dispute;
    }

    private async Task<Transaction> CreateTransactionAsync()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.PAYMENT_RECEIVED,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = BuyerSteam,
            ItemAssetId = "asset-" + Guid.NewGuid().ToString("N")[..8],
            ItemClassId = "CLASS-AK47-REDLINE",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 50.00m,
            CommissionRate = 0.03m,
            CommissionAmount = 1.50m,
            TotalAmount = 51.50m,
            SellerPayoutAddress = SellerWallet,
            PaymentTimeoutMinutes = 60,
            // The state the escalation is raised from: the round observed the
            // seller's asset leave without anything reaching the buyer.
            DeliveryEvidence = DeliveryEvidence.SELLER_ASSET_GONE,
            AcceptedAt = now.AddHours(-4),
            PaymentReceivedAt = now.AddHours(-2),
            DeliveryDeadline = now.AddMinutes(-1),
            CreatedAt = now,
            UpdatedAt = now,
        };
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return tx;
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
