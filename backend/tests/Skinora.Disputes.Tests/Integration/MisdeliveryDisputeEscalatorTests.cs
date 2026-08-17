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
    /// which is the one thing that lifts the hold.
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

        var outcome = await EscalateAsync(tx);

        Assert.Equal(expected, outcome);
        Assert.Empty(_outbox.Published);
        await Context.SaveChangesAsync();

        var dispute = await Context.Set<Dispute>().AsNoTracking()
            .SingleAsync(d => d.TransactionId == tx.Id);
        Assert.Equal(status, dispute.Status);
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

    private async Task<MisdeliveryEscalationOutcome> EscalateAsync(Transaction tx)
    {
        var tracked = await Context.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);
        var sut = new MisdeliveryDisputeEscalator(
            Context, _outbox, NullLogger<MisdeliveryDisputeEscalator>.Instance);
        return await sut.EscalateAsync(
            tracked, _clock.GetUtcNow().UtcDateTime, CancellationToken.None);
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
