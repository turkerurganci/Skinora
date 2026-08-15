using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Disputes.Application.AutoCheckers;
using Skinora.Disputes.Domain.Entities;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Delivery;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Disputes.Application.Disputes;

/// <summary>
/// T127 — adapter for <see cref="IDeliveryMisdeliveryEscalator"/>. Raises the
/// admin escalation 02 §9.2 / §10.1 require when a delivery-verification round
/// finds the item left the seller but never reached the buyer.
/// </summary>
/// <remarks>
/// <para>
/// <b>A system-opened dispute.</b> 02 §10.2 gives the opening right to the
/// buyer, and every other row in this table was opened by one. This one is the
/// exception the same document creates two sections earlier: "işlem sessizce
/// iptal edilmez, otomatik olarak dispute'a yükseltilir". The buyer cannot be
/// the opener because they may not even know anything went wrong — nothing
/// arrived, so from their side the transaction merely looks slow.
/// <c>OpenedByUserId</c> is therefore
/// <see cref="SeedConstants.SystemUserId"/>, the same SYSTEM actor the
/// timeout's own history row carries (06 §8.9).
/// </para>
/// <para>
/// <b>Idempotency.</b> <c>UQ_Disputes_TransactionId_Type</c> is unfiltered, so a
/// second DELIVERY row is impossible by construction (02 §10.2) — and the
/// scanner may reach the same transaction again. Each existing status has one
/// correct answer, enumerated in <see cref="MisdeliveryEscalationOutcome"/>.
/// </para>
/// <para>
/// <b>No SaveChanges.</b> Per the port contract, rows are added to the caller's
/// tracked context so the escalation and the evidence capture that justifies it
/// commit together.
/// </para>
/// </remarks>
public sealed class MisdeliveryDisputeEscalator : IDeliveryMisdeliveryEscalator
{
    private readonly AppDbContext _db;
    private readonly IOutboxService _outbox;
    private readonly ILogger<MisdeliveryDisputeEscalator> _logger;

    public MisdeliveryDisputeEscalator(
        AppDbContext db,
        IOutboxService outbox,
        ILogger<MisdeliveryDisputeEscalator> logger)
    {
        _db = db;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task<MisdeliveryEscalationOutcome> EscalateAsync(
        Transaction transaction,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // IgnoreQueryFilters so a soft-deleted row is seen too: the unique index
        // includes it, and inserting past it would throw at SaveChanges and take
        // the whole scanner batch down with it.
        var existing = await _db.Set<Dispute>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                d => d.TransactionId == transaction.Id && d.Type == DisputeType.DELIVERY,
                cancellationToken);

        // The buyer is the recipient of the stored result text, exactly as on
        // the buyer-opened path (WP17): one produce-time localization keeps the
        // dispute record and the notification in one language.
        var message = DisputeAutoCheckMessages.Localize(
            DisputeAutoCheckMessages.DeliveryAssetGoneNotArrived,
            await ResolveBuyerLocaleAsync(transaction.BuyerId, cancellationToken));

        if (existing is null)
        {
            var dispute = new Dispute
            {
                Id = Guid.NewGuid(),
                TransactionId = transaction.Id,
                OpenedByUserId = SeedConstants.SystemUserId,
                Type = DisputeType.DELIVERY,
                Status = DisputeStatus.ESCALATED,
                SystemCheckResult = message,
                CreatedAt = occurredAtUtc,
                UpdatedAt = occurredAtUtc,
            };
            _db.Set<Dispute>().Add(dispute);
            transaction.HasActiveDispute = true;

            await PublishAsync(transaction, dispute.Id, occurredAtUtc, cancellationToken);
            return MisdeliveryEscalationOutcome.Opened;
        }

        switch (existing.Status)
        {
            case DisputeStatus.OPEN:
                // The buyer opened one and the auto-checker left it OPEN so they
                // could escalate. The platform has now established the signature
                // itself, so it does not wait for them to press a button.
                existing.Status = DisputeStatus.ESCALATED;
                existing.SystemCheckResult = message;
                existing.UpdatedAt = occurredAtUtc;
                transaction.HasActiveDispute = true;

                await PublishAsync(transaction, existing.Id, occurredAtUtc, cancellationToken);
                return MisdeliveryEscalationOutcome.Promoted;

            case DisputeStatus.ESCALATED:
                // Already in the admin queue. Re-publishing would notify both
                // parties again on every scanner pass.
                transaction.HasActiveDispute = true;
                return MisdeliveryEscalationOutcome.AlreadyEscalated;

            default:
                // CLOSED or an admin resolution (RESOLVED_FOR_*). A human or an
                // auto-check has already answered this question; a second row is
                // forbidden by the unique index and re-opening a settled
                // decision is not a timeout's call. Logged so the contradiction
                // is visible rather than silent.
                _logger.LogWarning(
                    "Transaction {TransactionId}: misdelivery signature found at the delivery "
                    + "timeout, but its DELIVERY dispute {DisputeId} is already in {Status} — "
                    + "left untouched (02 §10.2)",
                    transaction.Id, existing.Id, existing.Status);
                return MisdeliveryEscalationOutcome.AlreadyResolved;
        }
    }

    /// <summary>
    /// Emit the two-party review notice (03 §6.3 step 5). <c>AutoEscalated</c>
    /// is <c>true</c>: like the WRONG_ITEM auto-escalation this is the platform
    /// putting the transaction under review, not a buyer asking it to, so both
    /// parties are told — including the seller, whose item is the subject.
    /// </summary>
    private async Task PublishAsync(
        Transaction transaction,
        Guid disputeId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken)
    {
        if (transaction.BuyerId is not { } buyerId)
        {
            // Unreachable: the misdelivery signature needs a baseline, which is
            // captured on entry to SELLER_CONFIRMED, which requires a buyer
            // (06 §3.5). Guarded because the alternative on a schema regression
            // is an exception that aborts the scanner's whole batch.
            _logger.LogError(
                "Transaction {TransactionId} has no BuyerId at the delivery timeout — "
                + "escalation recorded but the review notice was not published",
                transaction.Id);
            return;
        }

        await _outbox.PublishAsync(
            new DisputeEscalatedEvent(
                EventId: Guid.NewGuid(),
                DisputeId: disputeId,
                TransactionId: transaction.Id,
                Type: DisputeType.DELIVERY,
                SellerId: transaction.SellerId,
                BuyerId: buyerId,
                AutoEscalated: true,
                Detail: null,
                OccurredAt: occurredAtUtc),
            cancellationToken);
    }

    private async Task<string> ResolveBuyerLocaleAsync(
        Guid? buyerId, CancellationToken cancellationToken)
    {
        if (buyerId is not { } id) return "en";

        var locale = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => u.PreferredLanguage)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(locale) ? "en" : locale;
    }
}
