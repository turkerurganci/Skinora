using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.API.Services;
using Skinora.Disputes.Application.Admin;
using Skinora.Disputes.Application.Disputes;
using Skinora.Disputes.Domain.Entities;
using Skinora.Disputes.Infrastructure.Persistence;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Domain;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.API.Tests.Integration.Disputes;

/// <summary>
/// WP5 / T58 — end-to-end coverage for <see cref="AdminDisputeService"/>
/// (admin dispute resolution, 02 §10.4, 03 §6.4). Exercises both outcomes
/// (seller-favor uphold / buyer-favor unwind → REFUNDED), the per-state refund
/// fan-out, the guards (not-escalated, on-hold, validation, not-found), the
/// concurrent-dispute active-flag rule, and the list/detail surfaces against a
/// real SQL Server (so the new CHECK constraints are enforced).
/// </summary>
public class AdminDisputeServiceTests : IntegrationTestBase
{
    static AdminDisputeServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
        DisputesModuleDbRegistration.RegisterDisputesModule();
    }

    private const string SellerWallet = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL";
    private const string BuyerWallet = "TYyzABCDEFGHJKLMNPQRSTUVWXYZ234567";

    private FakeTimeProvider _clock = null!;
    private RecordingOutboxService _outbox = null!;
    private CapturingAuditLogger _audit = null!;
    private User _seller = null!;
    private User _buyer = null!;
    private User _admin = null!;
    private readonly Guid _adminId = Guid.NewGuid();

    protected override async Task SeedAsync(AppDbContext context)
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero));
        _outbox = new RecordingOutboxService();
        _audit = new CapturingAuditLogger();

        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000301",
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = SellerWallet,
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000302",
            SteamDisplayName = "Buyer",
        };
        _admin = new User
        {
            Id = _adminId,
            SteamId = "76561198000000303",
            SteamDisplayName = "Admin",
        };
        context.Set<User>().AddRange(_seller, _buyer, _admin);
        await context.SaveChangesAsync();
    }

    private AdminDisputeService BuildSut() =>
        new(Context, _outbox, _audit, _clock);

    // ---------- Seller-favor ----------

    [Fact]
    public async Task Resolve_SellerFavor_SetsResolvedForSeller_ClearsActiveDispute_NoStateChange()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.WRONG_ITEM);

        var outcome = await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(DisputeResolutionOutcome.SELLER_FAVOR, "Satıcı haklı bulundu."),
            ipAddress: "10.0.0.1", CancellationToken.None);

        Assert.Equal(AdminResolveDisputeStatus.Resolved, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Equal(DisputeStatus.RESOLVED_FOR_SELLER, outcome.Body!.Status);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, outcome.Body.TransactionStatus);
        Assert.False(outcome.Body.BuyerRefunded);

        var persistedDispute = await Context.Set<Dispute>().AsNoTracking().FirstAsync(d => d.Id == dispute.Id);
        Assert.Equal(DisputeStatus.RESOLVED_FOR_SELLER, persistedDispute.Status);
        Assert.Equal(_adminId, persistedDispute.AdminId);
        Assert.Equal("Satıcı haklı bulundu.", persistedDispute.AdminNote);
        Assert.NotNull(persistedDispute.ResolvedAt);

        var persistedTx = await Context.Set<Transaction>().AsNoTracking().FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persistedTx.Status); // no transition
        Assert.False(persistedTx.HasActiveDispute);                         // unblocks WP1 payout

        // Only the notification event — no refund/return.
        Assert.Single(_outbox.Published);
        Assert.IsType<DisputeResolvedEvent>(_outbox.Published[0]);
        Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.DISPUTE_RESOLVED, _audit.Entries[0].Action);
    }

    /// <summary>
    /// <b>T127 finding B3.</b> The resolution notice is addressed to the
    /// transaction's buyer, not to whoever opened the dispute.
    /// </summary>
    /// <remarks>
    /// Those were the same party for as long as 02 §10.2 left the buyer as the
    /// only opener. T127 added the exception the same document requires: a
    /// misdelivery signature the platform establishes itself, opened by SYSTEM
    /// because the buyer may not even know anything went wrong — nothing
    /// arrived, so from their side the transaction merely looks slow. Reading
    /// the opener as the buyer sends DISPUTE_RESULT to the SYSTEM account and
    /// the real buyer is never told how their dispute ended.
    /// </remarks>
    [Fact]
    public async Task Resolve_OfASystemOpenedDispute_NotifiesTheRealBuyer_NotTheOpener()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED, withPayment: true);
        var dispute = await CreateDisputeAsync(
            tx, DisputeType.DELIVERY, DisputeStatus.ESCALATED,
            openedByUserId: SeedConstants.SystemUserId);

        await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(DisputeResolutionOutcome.SELLER_FAVOR, "Satıcı haklı bulundu."),
            ipAddress: null, CancellationToken.None);

        var resolved = Assert.Single(_outbox.Published.OfType<DisputeResolvedEvent>());
        Assert.Equal(_buyer.Id, resolved.BuyerId);
        Assert.NotEqual(SeedConstants.SystemUserId, resolved.BuyerId);
        Assert.Equal(_seller.Id, resolved.SellerId);
    }

    [Fact]
    public async Task Resolve_SellerFavor_WithOtherActiveDispute_KeepsActiveDisputeTrue()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        var escalated = await CreateEscalatedDisputeAsync(tx, DisputeType.WRONG_ITEM);
        // A second, still-active dispute of a different type.
        await CreateDisputeAsync(tx, DisputeType.DELIVERY, DisputeStatus.OPEN);

        await BuildSut().ResolveAsync(
            _adminId, escalated.Id,
            new AdminResolveDisputeRequest(DisputeResolutionOutcome.SELLER_FAVOR, "note"),
            ipAddress: null, CancellationToken.None);

        var persistedTx = await Context.Set<Transaction>().AsNoTracking().FirstAsync(t => t.Id == tx.Id);
        Assert.True(persistedTx.HasActiveDispute); // sibling DELIVERY dispute still OPEN
    }

    // ---------- Buyer-favor ----------

    [Fact]
    public async Task Resolve_BuyerFavor_AtItemDelivered_RefundsBuyer_NoItemReturn_TransitionsToRefunded()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withPayment: true);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.WRONG_ITEM);

        var outcome = await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(
                DisputeResolutionOutcome.BUYER_FAVOR,
                "Alıcı haklı; iade edilecek.",
                // T131 — delivery is established here, so the ruling is an
                // override and cannot be made without its own justification.
                OverrideReason: "Steam Support kaydı trade'in geri alındığını gösteriyor."),
            ipAddress: "10.0.0.1", CancellationToken.None);

        Assert.Equal(AdminResolveDisputeStatus.Resolved, outcome.Status);
        Assert.Equal(TransactionStatus.REFUNDED, outcome.Body!.TransactionStatus);
        Assert.True(outcome.Body.BuyerRefunded);

        var persistedTx = await Context.Set<Transaction>().AsNoTracking().FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.REFUNDED, persistedTx.Status);
        Assert.Equal(CancelledByType.ADMIN, persistedTx.CancelledBy);
        Assert.NotNull(persistedTx.CancelReason);
        Assert.NotNull(persistedTx.CancelledAt);
        Assert.False(persistedTx.HasActiveDispute);

        var persistedDispute = await Context.Set<Dispute>().AsNoTracking().FirstAsync(d => d.Id == dispute.Id);
        Assert.Equal(DisputeStatus.RESOLVED_FOR_BUYER, persistedDispute.Status);

        // Buyer payment refund queued. There is no item-return leg in v3.0 —
        // the platform never held the item (02 §9).
        Assert.Single(_outbox.Published.OfType<PaymentRefundToBuyerRequestedEvent>());
        Assert.Single(_outbox.Published.OfType<DisputeResolvedEvent>());
    }

    [Fact]
    public async Task Resolve_BuyerFavor_AtPaymentReceived_RefundsBuyer()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED, withPayment: true);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.PAYMENT);

        var outcome = await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(DisputeResolutionOutcome.BUYER_FAVOR, "note"),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(AdminResolveDisputeStatus.Resolved, outcome.Status);
        var persistedTx = await Context.Set<Transaction>().AsNoTracking().FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.REFUNDED, persistedTx.Status);

        // Only the money moves: the item never left the seller's inventory in
        // this state, so there is nothing to return (02 §9).
        Assert.Single(_outbox.Published.OfType<PaymentRefundToBuyerRequestedEvent>());

        // T131 — no delivery was established here, so the ruling overrode
        // nothing and the exception register stays empty.
        var persistedDispute = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == dispute.Id);
        Assert.Null(persistedDispute.ResolutionOverrideReason);
    }

    // ---------- T131: the override gate (02 §10.4, 03 §6.4) ----------

    /// <summary>
    /// At ITEM_DELIVERED the platform has already established delivery — the
    /// state has one entry edge and its guard demands 02 §9.2 evidence. Ruling
    /// for the buyer there reverses the platform's own finding and, since there
    /// is no item leg, hands the whole loss to a seller who cannot recover the
    /// item. 03 §6.4 makes that an exception whose reason is recorded
    /// separately; without one the ruling is refused.
    /// </summary>
    [Fact]
    public async Task Resolve_BuyerFavor_AtItemDelivered_WithoutOverrideReason_IsRejected()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withPayment: true);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.WRONG_ITEM);

        var outcome = await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(DisputeResolutionOutcome.BUYER_FAVOR, "note"),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(AdminResolveDisputeStatus.ValidationFailed, outcome.Status);
        Assert.Equal(DisputeErrorCodes.OverrideReasonRequired, outcome.ErrorCode);

        // Nothing moved: the refusal is a guard, not a partial resolution.
        var persistedTx = await Context.Set<Transaction>().AsNoTracking().FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persistedTx.Status);
        var persistedDispute = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == dispute.Id);
        Assert.Equal(DisputeStatus.ESCALATED, persistedDispute.Status);
        Assert.Empty(_outbox.Published);
    }

    /// <summary>
    /// The floor exists so the field cannot be satisfied by typing past it: a
    /// gate that accepts "ok" records nothing, and the gate is there precisely
    /// because this ruling has to be explainable after the money is gone.
    /// </summary>
    [Fact]
    public async Task Resolve_BuyerFavor_AtItemDelivered_WithATokenOverrideReason_IsRejected()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withPayment: true);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.DELIVERY);

        var outcome = await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(
                DisputeResolutionOutcome.BUYER_FAVOR, "note", OverrideReason: "  ok  "),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(AdminResolveDisputeStatus.ValidationFailed, outcome.Status);
        Assert.Equal(DisputeErrorCodes.OverrideReasonRequired, outcome.ErrorCode);
    }

    /// <summary>
    /// The accepted ruling records its reason in two places on purpose: the
    /// column is where the next admin reads it, the audit row is where it cannot
    /// be edited afterwards (06 §3.20).
    /// </summary>
    [Fact]
    public async Task Resolve_BuyerFavor_AtItemDelivered_PersistsTheOverrideReason_AndAuditsIt()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withPayment: true);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.DELIVERY);
        const string reason = "Alıcı Steam Support kaydı sundu; trade geri alınmış (bilet #4412).";

        var outcome = await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(
                DisputeResolutionOutcome.BUYER_FAVOR, "note", OverrideReason: $"  {reason}  "),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(AdminResolveDisputeStatus.Resolved, outcome.Status);

        var persistedDispute = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == dispute.Id);
        Assert.Equal(reason, persistedDispute.ResolutionOverrideReason);

        // Parsed rather than substring-matched: the serializer escapes non-ASCII
        // (ı for "ı" and so on), so a raw Contains would pass or fail on the
        // reason's alphabet rather than on whether it was recorded.
        var audit = Assert.Single(_audit.Entries, e => e.Action == AuditAction.DISPUTE_RESOLVED);
        using var auditJson = JsonDocument.Parse(audit.NewValue!);
        Assert.Equal(reason, auditJson.RootElement.GetProperty("OverrideReason").GetString());
    }

    /// <summary>
    /// A seller-favour ruling upholds the platform's own finding, so there is
    /// nothing to override and nothing to justify.
    /// </summary>
    [Fact]
    public async Task Resolve_SellerFavor_AtItemDelivered_NeedsNoOverrideReason()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withPayment: true);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.DELIVERY);

        var outcome = await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(DisputeResolutionOutcome.SELLER_FAVOR, "note"),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(AdminResolveDisputeStatus.Resolved, outcome.Status);
        var persistedDispute = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == dispute.Id);
        Assert.Equal(DisputeStatus.RESOLVED_FOR_SELLER, persistedDispute.Status);
        Assert.Null(persistedDispute.ResolutionOverrideReason);
    }

    /// <summary>
    /// A reason sent where nothing was overridden is dropped rather than stored.
    /// That is what keeps a non-NULL column meaning exactly one thing — a ruling
    /// made against the platform's own proof of delivery — so the register of
    /// exceptions stays searchable.
    /// </summary>
    [Fact]
    public async Task Resolve_OverrideReason_OnARulingThatOverrodeNothing_IsNotStored()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED, withPayment: true);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.PAYMENT);

        var outcome = await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(
                DisputeResolutionOutcome.BUYER_FAVOR, "note",
                OverrideReason: "Bu gerekçenin geçersiz kılacağı bir kanıt yok."),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(AdminResolveDisputeStatus.Resolved, outcome.Status);
        var persistedDispute = await Context.Set<Dispute>().AsNoTracking()
            .FirstAsync(d => d.Id == dispute.Id);
        Assert.Null(persistedDispute.ResolutionOverrideReason);
    }

    /// <summary>
    /// The gate is ordered after the state guards, so an admin is never asked to
    /// justify a ruling that would have been refused anyway.
    /// </summary>
    [Fact]
    public async Task Resolve_OnAHeldTransaction_ReportsTheHold_NotTheMissingOverride()
    {
        var tx = await CreateTransactionAsync(
            TransactionStatus.ITEM_DELIVERED, withPayment: true, withHold: true);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.DELIVERY);

        var outcome = await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(DisputeResolutionOutcome.BUYER_FAVOR, "note"),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(AdminResolveDisputeStatus.TransactionOnHold, outcome.Status);
        Assert.Equal(DisputeErrorCodes.TransactionOnHold, outcome.ErrorCode);
    }

    // ---------- Guards ----------

    [Fact]
    public async Task Resolve_NonEscalatedDispute_ReturnsNotEscalated_NoMutation()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        var dispute = await CreateDisputeAsync(tx, DisputeType.WRONG_ITEM, DisputeStatus.OPEN);

        var outcome = await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(DisputeResolutionOutcome.SELLER_FAVOR, "note"),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(AdminResolveDisputeStatus.NotEscalated, outcome.Status);
        Assert.Equal(DisputeErrorCodes.NotEscalated, outcome.ErrorCode);

        var persisted = await Context.Set<Dispute>().AsNoTracking().FirstAsync(d => d.Id == dispute.Id);
        Assert.Equal(DisputeStatus.OPEN, persisted.Status);
        Assert.Null(persisted.AdminId);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task Resolve_TransactionOnHold_ReturnsOnHold()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withHold: true);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.WRONG_ITEM);

        var outcome = await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(DisputeResolutionOutcome.SELLER_FAVOR, "note"),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(AdminResolveDisputeStatus.TransactionOnHold, outcome.Status);
        Assert.Equal(DisputeErrorCodes.TransactionOnHold, outcome.ErrorCode);

        var persisted = await Context.Set<Dispute>().AsNoTracking().FirstAsync(d => d.Id == dispute.Id);
        Assert.Equal(DisputeStatus.ESCALATED, persisted.Status); // untouched
    }

    [Fact]
    public async Task Resolve_MissingNote_ReturnsValidationFailed()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.WRONG_ITEM);

        var outcome = await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(DisputeResolutionOutcome.SELLER_FAVOR, "   "),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(AdminResolveDisputeStatus.ValidationFailed, outcome.Status);
        Assert.Equal(DisputeErrorCodes.ValidationError, outcome.ErrorCode);
    }

    [Fact]
    public async Task Resolve_DisputeNotFound_ReturnsNotFound()
    {
        var outcome = await BuildSut().ResolveAsync(
            _adminId, Guid.NewGuid(),
            new AdminResolveDisputeRequest(DisputeResolutionOutcome.SELLER_FAVOR, "note"),
            ipAddress: null, CancellationToken.None);

        Assert.Equal(AdminResolveDisputeStatus.NotFound, outcome.Status);
        Assert.Equal(DisputeErrorCodes.DisputeNotFound, outcome.ErrorCode);
    }

    // ---------- List / detail ----------

    [Fact]
    public async Task List_DefaultsToEscalated_AndFiltersByType()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        await CreateEscalatedDisputeAsync(tx, DisputeType.WRONG_ITEM);
        await CreateDisputeAsync(tx, DisputeType.DELIVERY, DisputeStatus.OPEN); // not escalated

        var page = await BuildSut().ListAsync(
            new AdminDisputeListQuery(Status: null, Type: null, Page: 1, PageSize: 20),
            CancellationToken.None);

        Assert.Equal(1, page.TotalCount); // only the ESCALATED row
        Assert.Equal(DisputeStatus.ESCALATED, page.Items[0].Status);
        Assert.Equal("AK-47 | Redline", page.Items[0].ItemName);
        Assert.Equal(_buyer.Id, page.Items[0].OpenedBy.UserId);
    }

    [Fact]
    public async Task Get_ReturnsDetail_WithTransactionAndParties()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.WRONG_ITEM);

        var detail = await BuildSut().GetAsync(dispute.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(dispute.Id, detail!.Id);
        Assert.Equal(DisputeStatus.ESCALATED, detail.Status);
        Assert.Equal(tx.Id, detail.Transaction.Id);
        Assert.Equal("Seller", detail.Transaction.Seller.DisplayName);
        Assert.Equal("Buyer", detail.Transaction.Buyer!.DisplayName);
    }

    [Fact]
    public async Task Get_UnknownDispute_ReturnsNull()
    {
        var detail = await BuildSut().GetAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Null(detail);
    }

    /// <summary>
    /// T130 wrote the arrived item's name onto the dispute; T131's job is that
    /// AD28 carries it to the screen. Asserted here because until now nothing
    /// checked the DTO — the column had a writer and a reader and no test
    /// between them (T130 validation finding N2).
    /// </summary>
    [Fact]
    public async Task Get_ReturnsTheDeliveredItemName_ForTheAdminToCompare()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.WRONG_ITEM);
        dispute.DeliveredItemName = "AWP | Asiimov (Field-Tested)";
        await Context.SaveChangesAsync();

        var detail = await BuildSut().GetAsync(dispute.Id, CancellationToken.None);

        Assert.Equal("AWP | Asiimov (Field-Tested)", detail!.DeliveredItemName);
        // The other half of the comparison travels in the same response, so the
        // screen never has to stitch two fetches together (03 §6.3 Sonuç B).
        Assert.Equal("AK-47 | Redline", detail.Transaction.ItemName);
    }

    /// <summary>
    /// T131 — AD28 answers the override question itself so the screen can ask
    /// for the justification while the admin is deciding, instead of letting
    /// them discover the rule from a rejected submission. One implementation of
    /// the rule (the service); the client only renders the answer.
    /// </summary>
    [Theory]
    [InlineData(TransactionStatus.ITEM_DELIVERED, true)]
    [InlineData(TransactionStatus.PAYMENT_RECEIVED, false)]
    [InlineData(TransactionStatus.SELLER_CONFIRMED, false)]
    public async Task Get_AnswersWhetherABuyerFavorRulingWouldNeedAnOverride(
        TransactionStatus status, bool expected)
    {
        var tx = await CreateTransactionAsync(status, withPayment: true);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.DELIVERY);

        var detail = await BuildSut().GetAsync(dispute.Id, CancellationToken.None);

        Assert.Equal(expected, detail!.BuyerFavorRequiresOverride);
    }

    /// <summary>
    /// A recorded override travels back out on the detail, so the exception is
    /// readable where the decision is read rather than only in the audit table.
    /// </summary>
    [Fact]
    public async Task Get_ReturnsARecordedOverrideReason()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withPayment: true);
        var dispute = await CreateEscalatedDisputeAsync(tx, DisputeType.DELIVERY);
        const string reason = "Steam Support kaydı trade'in geri alındığını gösteriyor.";

        await BuildSut().ResolveAsync(
            _adminId, dispute.Id,
            new AdminResolveDisputeRequest(
                DisputeResolutionOutcome.BUYER_FAVOR, "note", OverrideReason: reason),
            ipAddress: null, CancellationToken.None);
        Context.ChangeTracker.Clear();

        var detail = await BuildSut().GetAsync(dispute.Id, CancellationToken.None);

        Assert.Equal(reason, detail!.ResolutionOverrideReason);
    }

    // ---------- helpers ----------

    private async Task<Transaction> CreateTransactionAsync(
        TransactionStatus status, bool withPayment = false, bool withHold = false)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = _buyer.SteamId,
            BuyerRefundAddress = BuyerWallet,
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
            PaymentReceivedAt = withPayment ? now.AddMinutes(-30) : null,
            CreatedAt = now,
            UpdatedAt = now,
            AcceptedAt = now.AddMinutes(-60),
        };

        if (withHold)
        {
            tx.IsOnHold = true;
            tx.EmergencyHoldAt = now;
            tx.EmergencyHoldReason = "investigation";
            tx.EmergencyHoldByAdminId = _adminId;
            tx.TimeoutFrozenAt = now;
            tx.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
            tx.TimeoutRemainingSeconds = 3600;
        }

        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx;
    }

    private Task<Dispute> CreateEscalatedDisputeAsync(Transaction tx, DisputeType type) =>
        CreateDisputeAsync(tx, type, DisputeStatus.ESCALATED);

    private async Task<Dispute> CreateDisputeAsync(
        Transaction tx, DisputeType type, DisputeStatus status, Guid? openedByUserId = null)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var dispute = new Dispute
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            // Defaults to the buyer — the only opener 02 §10.2 allows until
            // T127's SYSTEM-opened misdelivery escalation.
            OpenedByUserId = openedByUserId ?? _buyer.Id,
            Type = type,
            Status = status,
            UserDescription = "Yanlış item teslim edildi",
            ResolvedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Context.Set<Dispute>().Add(dispute);

        // Mark the transaction active so resolution can clear it (OPEN/ESCALATED).
        if (status is DisputeStatus.OPEN or DisputeStatus.ESCALATED)
        {
            var tracked = await Context.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);
            tracked.HasActiveDispute = true;
        }

        await Context.SaveChangesAsync();
        return dispute;
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

    private sealed class CapturingAuditLogger : IAuditLogger
    {
        public List<AuditLogEntry> Entries { get; } = [];

        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
