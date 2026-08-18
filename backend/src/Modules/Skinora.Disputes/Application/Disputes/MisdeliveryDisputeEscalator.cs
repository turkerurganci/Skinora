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
        DateTime signatureFirstObservedAtUtc,
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

            case DisputeStatus.RESOLVED_FOR_SELLER:
            case DisputeStatus.RESOLVED_FOR_BUYER:
                // T131 (T127 finding G3) — an ADMIN has ruled on this row. The
                // dispute is left untouched exactly as below, but the caller is
                // told so, because this is the one case where its hold must
                // lift: 02 §9.2 forbids a SILENT cancellation, and a human has
                // now read the case. Holding on past a ruling is what left the
                // buyer's money in escrow with no automatic exit.
                //
                // T131 fix round (validation finding N2) — but only if the
                // ruling was made WITH this signature in front of it. The
                // release is granted for one reason and one reason only: a human
                // read the case. An admin who ruled BEFORE the signature existed
                // read a different case, so treating their ruling as consent to
                // cancel would reverse it on evidence they never saw.
                if (RuledWithoutTheSignature(existing, signatureFirstObservedAtUtc))
                {
                    return await ReEscalateAsync(
                        transaction, existing, message, occurredAtUtc, signatureFirstObservedAtUtc, cancellationToken);
                }

                _logger.LogInformation(
                    "Transaction {TransactionId}: misdelivery signature stands, but its DELIVERY "
                    + "dispute {DisputeId} was already ruled on by an admin ({Status}) — the "
                    + "delivery timeout is released to its normal course (03 §6.4)",
                    transaction.Id, existing.Id, existing.Status);
                return MisdeliveryEscalationOutcome.AlreadyRuledByAdmin;

            default:
                // CLOSED — the system's own auto-check answered this question
                // (06 §2.10). A second row is forbidden by the unique index and
                // re-opening a settled decision is not a timeout's call. Unlike
                // the admin arm above this does NOT release the hold: nobody
                // looked, so a cancellation here would still be silent. Logged
                // so the contradiction is visible rather than silent.
                _logger.LogWarning(
                    "Transaction {TransactionId}: misdelivery signature found at the delivery "
                    + "timeout, but its DELIVERY dispute {DisputeId} is already in {Status} — "
                    + "left untouched (02 §10.2)",
                    transaction.Id, existing.Id, existing.Status);
                return MisdeliveryEscalationOutcome.AlreadyResolved;
        }
    }

    /// <summary>
    /// Whether the admin resolution on <paramref name="dispute"/> was reached
    /// without the misdelivery signature on record (T131 finding N2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparison is <c>ResolvedAt</c> against when the signature was first
    /// observed. A signature recorded STRICTLY BEFORE the ruling is one the
    /// admin had: establishing it sets the dispute to ESCALATED and writes the
    /// finding into <c>SystemCheckResult</c>, so it is on the very screen AD28
    /// renders. One recorded at or after the ruling is not.
    /// </para>
    /// <para>
    /// The two ambiguous inputs — equal timestamps, and a NULL
    /// <c>ResolvedAt</c> (impossible while the status is RESOLVED_FOR_* per
    /// <c>CK_Disputes_Resolved_ResolvedAt</c>, so a schema regression) — both
    /// read as "cannot establish that they saw it", because the two errors are
    /// not symmetric: sending a case back to the admin queue costs a review,
    /// while a wrong release cancels the transaction and refunds the buyer
    /// against a ruling nobody re-read.
    /// </para>
    /// </remarks>
    private static bool RuledWithoutTheSignature(
        Dispute dispute, DateTime signatureFirstObservedAtUtc)
        => dispute.ResolvedAt is not { } ruledAt || ruledAt <= signatureFirstObservedAtUtc;

    /// <summary>
    /// Put a resolved dispute back into the admin queue because the platform
    /// established the misdelivery signature after the ruling (T131 finding N2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the same row, not a second one: 02 §10.2's "aynı türde dispute
    /// tekrar açılamaz" and <c>UQ_Disputes_TransactionId_Type</c> both forbid an
    /// insert, and neither is being worked around. What justifies re-opening it
    /// is that the platform is bringing a NEW fact — it watched the item leave
    /// the seller after the ruling — rather than asking for the old one to be
    /// weighed again. The CLOSED arm below is deliberately not treated this way:
    /// there the system answered its own question, and nothing new has arrived.
    /// </para>
    /// <para>
    /// <c>ResolvedAt</c> is cleared because the row is no longer resolved and
    /// <c>CK_Disputes_Resolved_ResolvedAt</c> pairs the two; the previous ruling
    /// survives where it cannot be edited — the <c>DISPUTE_RESOLVED</c> audit
    /// entry (06 §3.20, append-only). <c>AdminNote</c> and <c>AdminId</c> are
    /// kept, so that whoever rules next can read the earlier ruling first.
    /// </para>
    /// </remarks>
    private async Task<MisdeliveryEscalationOutcome> ReEscalateAsync(
        Transaction transaction,
        Dispute dispute,
        string message,
        DateTime occurredAtUtc,
        DateTime signatureFirstObservedAtUtc,
        CancellationToken cancellationToken)
    {
        var previousStatus = dispute.Status;
        var previousResolvedAt = dispute.ResolvedAt;

        dispute.Status = DisputeStatus.ESCALATED;
        dispute.SystemCheckResult = message;
        dispute.ResolvedAt = null;
        dispute.UpdatedAt = occurredAtUtc;
        transaction.HasActiveDispute = true;

        await PublishAsync(transaction, dispute.Id, occurredAtUtc, cancellationToken);

        _logger.LogWarning(
            "Transaction {TransactionId}: DELIVERY dispute {DisputeId} was ruled {PreviousStatus} "
            + "at {ResolvedAt}, but the misdelivery signature was first observed at "
            + "{SignatureObservedAt} — the ruling was made without it, so the dispute goes back "
            + "to the admin queue and the delivery timeout keeps holding (02 §9.2, 03 §6.4)",
            transaction.Id, dispute.Id, previousStatus, previousResolvedAt, signatureFirstObservedAtUtc);

        return MisdeliveryEscalationOutcome.ReEscalatedAfterRuling;
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
