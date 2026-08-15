using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// T127 — default <see cref="IDeliveryTimeoutRound"/>. Turns one
/// <see cref="DeliveryVerdict"/> into the action 03 §4.4 step 1 prescribes.
/// </summary>
/// <remarks>
/// <para>
/// The mapping, and why each arm is what it is:
/// </para>
/// <list type="table">
///   <item>
///     <term>Delivered</term>
///     <description>
///       Evidence sufficient and nothing gates it → <c>ITEM_DELIVERED</c>. This
///       is the arm the whole round exists for: it stops the platform from
///       refunding a buyer who already has the item.
///     </description>
///   </item>
///   <item>
///     <term>InventoryEvidencePendingReview</term>
///     <description>
///       Held. The evidence says the item ARRIVED, so cancelling would be
///       plainly wrong; the launch gate only withholds the automatic payout
///       until a human has read the capture (DEPLOY_RUNBOOK §H).
///     </description>
///   </item>
///   <item>
///     <term>MisdeliverySignature</term>
///     <description>
///       Held and escalated. 02 §9.2 is explicit that this case "işlem sessizce
///       iptal edilmez" — the item went somewhere, and where it went is an admin
///       question, not a timeout's.
///     </description>
///   </item>
///   <item>
///     <term>NoMovement / Inconclusive</term>
///     <description>
///       Cancel only when the platform can prove the seller still holds the
///       item; otherwise held. See <see cref="SellerProvenToStillHoldTheItem"/>.
///     </description>
///   </item>
/// </list>
/// </remarks>
public sealed class DeliveryTimeoutRound : IDeliveryTimeoutRound
{
    private readonly AppDbContext _db;
    private readonly IDeliveryVerificationService _verification;
    private readonly IDeliveryMisdeliveryEscalator _escalator;
    private readonly IOutboxService _outbox;
    private readonly ILogger<DeliveryTimeoutRound> _logger;
    private readonly TimeProvider _clock;

    public DeliveryTimeoutRound(
        AppDbContext db,
        IDeliveryVerificationService verification,
        IDeliveryMisdeliveryEscalator escalator,
        IOutboxService outbox,
        ILogger<DeliveryTimeoutRound> logger,
        TimeProvider clock)
    {
        _db = db;
        _verification = verification;
        _escalator = escalator;
        _outbox = outbox;
        _logger = logger;
        _clock = clock;
    }

    public async Task<DeliveryTimeoutDecision> RunAsync(
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        // Stamped before any arm runs, including the short-circuits below: the
        // scanner's fairness window asks "when did we last LOOK at this row",
        // not "when did we last conclude something" (T127 validation finding
        // B2). A round that reaches no conclusion is exactly the one that must
        // step aside so a never-examined delivery can take its slot.
        transaction.DeliveryRoundAt = nowUtc;

        // ---------- Re-entry: the signature is already on record ----------
        // A held transaction stays overdue forever, so every scanner pass
        // reaches it again. The verification engine already short-circuits when
        // the recorded evidence is SUFFICIENT (no Steam read), but the
        // misdelivery signature is not sufficiency — it is a finding, and
        // re-deriving it would spend two rate-limited inventory reads per pass
        // on a question that already has an owner.
        //
        // The gate asks the CAPTURE, not the evidence flags (T127 validation
        // finding B1). Both name the same three bits, but only one of them
        // carries the qualifier the engine applies: a signature needs
        // sellerSideKnown && buyerSideKnown (DeliveryVerificationService), so
        // "seller's asset gone + buyer's inventory private" raises
        // SELLER_ASSET_GONE and still verdicts Inconclusive — the platform
        // cannot see, which 08 §2.3 forbids reading as a negative finding.
        // Gating on the bare flag test made the next pass escalate a seller who
        // may well have delivered; gating on a recorded MisdeliverySignature
        // verdict cannot, because that verdict is the engine's own conclusion.
        //
        // The escalation is re-asserted rather than assumed. It is idempotent
        // and costs one indexed read, and that turns the only partial-commit
        // hazard in this file into a self-healing one: if a previous pass
        // committed the capture but its escalation was rolled back, this pass
        // raises the dispute instead of skipping past a signature nobody was
        // ever told about.
        if (await MisdeliveryAlreadyConcludedAsync(transaction.Id, cancellationToken))
        {
            var reasserted = await _escalator.EscalateAsync(transaction, nowUtc, cancellationToken);
            _logger.LogDebug(
                "Transaction {TransactionId}: misdelivery signature already recorded — no Steam "
                + "read spent, escalation re-asserted ({Outcome})",
                transaction.Id, reasserted);
            return DeliveryTimeoutDecision.Held;
        }

        // Fresh, never cached: this round decides whether money moves, and the
        // sidecar's 120-second cache can still show an item the seller traded
        // away two minutes ago (02 §10.1).
        var result = await _verification.VerifyAsync(
            transaction, InventoryReadFreshness.Fresh, cancellationToken);

        // Persisted on every arm, including the ones that conclude nothing:
        // evidence flags are observations, and recording them is what lets the
        // next round (and the buyer's own confirm-receipt) build on this one
        // instead of re-deriving it. The timestamp is the field that must wait —
        // see the Delivered arm.
        transaction.DeliveryEvidence = result.Evidence;

        switch (result.Verdict)
        {
            case DeliveryVerdict.Delivered:
                return await DeliverAsync(transaction, result, nowUtc, cancellationToken);

            case DeliveryVerdict.InventoryEvidencePendingReview:
                return HoldForReview(transaction, result, nowUtc);

            case DeliveryVerdict.MisdeliverySignature:
                return await EscalateAsync(transaction, result, nowUtc, cancellationToken);

            // 03 §4.4 step 1: "Kanıt yoksa → aşağıdaki iptal akışı işler". The
            // qualifier below is what separates "no evidence" from "no look":
            // 08 §2.3 forbids treating an unreadable inventory as a negative
            // finding, and the expensive direction is not symmetric — a wrong
            // cancel refunds the buyer and blames a seller who may have
            // delivered, while a wrong hold only delays.
            case DeliveryVerdict.NoMovement:
            case DeliveryVerdict.Inconclusive:
            default:
                return Undelivered(transaction, result);
        }
    }

    /// <summary>
    /// Fire <c>DeliverItem</c> and record the round.
    /// </summary>
    private async Task<DeliveryTimeoutDecision> DeliverAsync(
        Transaction transaction,
        DeliveryVerificationResult result,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Captured so a refused trigger can be rolled back field by field. This
        // matters here in a way it does not in the confirm-receipt endpoint
        // (T126): that caller owns its SaveChanges and can simply return without
        // saving, while this one shares a unit of work with the rest of the
        // scan — a half-stamped transaction would be committed by somebody
        // else's cancellation. And the half that would survive is precisely
        // DeliveryVerifiedAt, the field holding the launch gate shut.
        var previousVerifiedAt = transaction.DeliveryVerifiedAt;
        var previousDeliveredAssetId = transaction.DeliveredBuyerAssetId;
        var previousStatus = transaction.Status;

        // 02 §9.2 invariant: stamped BEFORE the guard runs (HasDeliveryEvidence
        // reads IsSufficientForDelivery() && DeliveryVerifiedAt.HasValue).
        transaction.DeliveryVerifiedAt = nowUtc;

        // 06 §8.4 — best-effort audit material for WRONG_ITEM handling, never a
        // guard. Only ever written once: a later round's candidate must not
        // overwrite an id an earlier observation already named.
        if (result.CandidateDeliveredAssetId is { } candidate
            && string.IsNullOrEmpty(transaction.DeliveredBuyerAssetId))
        {
            transaction.DeliveredBuyerAssetId = candidate;
        }

        var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
        try
        {
            machine.Fire(TransactionTrigger.DeliverItem);
        }
        catch (DomainException ex)
        {
            transaction.DeliveryVerifiedAt = previousVerifiedAt;
            transaction.DeliveredBuyerAssetId = previousDeliveredAssetId;

            _logger.LogError(ex,
                "Transaction {TransactionId}: delivery timeout round proved delivery but "
                + "DeliverItem was refused ({ErrorCode}) — the stamp was rolled back and the "
                + "transaction stays in {Status}",
                transaction.Id, ex.ErrorCode, transaction.Status);
            return DeliveryTimeoutDecision.Held;
        }

        // WP15 — audit-trail row (06 §3.6). SYSTEM actor: unlike confirm-receipt
        // this conclusion is the platform's own inference, not a user action.
        TransactionHistoryRecorder.Record(
            _db, transaction, previousStatus, TransactionTrigger.DeliverItem,
            ActorType.SYSTEM, SeedConstants.SystemUserId, nowUtc);

        DeliveryEvidenceCaptureRecorder.Record(_db, transaction, result, nowUtc);

        // Feeds the WP9 realtime relay — 03 §3.5 step 9 is explicit that
        // ITEM_DELIVERED has no inbox/email type of its own (06 §2.13 defines
        // none). Published into the same unit of work as the transition so no
        // client is told about a delivery that rolled back.
        //
        // DeliveryDeadline is deliberately left as it stands: the scanner's
        // query filters on PAYMENT_RECEIVED, so leaving that state is what takes
        // this row out of it, and the column keeps its value as the record of
        // the window the seller actually had.
        await _outbox.PublishAsync(
            new TransactionStatusChangedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                FromStatus: previousStatus,
                ToStatus: transaction.Status,
                OccurredAt: nowUtc),
            cancellationToken);

        _logger.LogInformation(
            "Transaction {TransactionId}: delivery deadline passed but the verification round "
            + "proved delivery — PAYMENT_RECEIVED → ITEM_DELIVERED instead of a cancellation, "
            + "evidence {Evidence} (05 §4.4, 02 §9.2)",
            transaction.Id, transaction.DeliveryEvidence);

        return DeliveryTimeoutDecision.Delivered;
    }

    /// <summary>
    /// The launch gate arm (T125 finding F3 — the twin of the same clause in
    /// <c>DeliveryConfirmationService</c>).
    /// </summary>
    /// <remarks>
    /// <c>DeliveryVerifiedAt</c> is deliberately NOT stamped. The state-machine
    /// guard knows nothing about the gate, so stamping it here would open the
    /// gate silently and release money on an inference no human has read
    /// (DEPLOY_RUNBOOK §H). Cancelling is equally forbidden and for a simpler
    /// reason: the evidence says the item reached the buyer.
    /// </remarks>
    private DeliveryTimeoutDecision HoldForReview(
        Transaction transaction,
        DeliveryVerificationResult result,
        DateTime nowUtc)
    {
        DeliveryEvidenceCaptureRecorder.Record(_db, transaction, result, nowUtc);

        _logger.LogWarning(
            "Transaction {TransactionId}: delivery deadline passed with sufficient inventory "
            + "evidence but the launch gate is closed — held for human review, neither delivered "
            + "nor cancelled, buyer funds stay in escrow (DEPLOY_RUNBOOK §H.2)",
            transaction.Id);

        return DeliveryTimeoutDecision.Held;
    }

    /// <summary>
    /// The 02 §10.1 escalation arm: the item left the seller and never arrived.
    /// </summary>
    private async Task<DeliveryTimeoutDecision> EscalateAsync(
        Transaction transaction,
        DeliveryVerificationResult result,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        DeliveryEvidenceCaptureRecorder.Record(_db, transaction, result, nowUtc);

        var outcome = await _escalator.EscalateAsync(transaction, nowUtc, cancellationToken);

        _logger.LogWarning(
            "Transaction {TransactionId}: delivery deadline passed on a misdelivery signature — "
            + "seller asset {AssetId} is gone but the buyer's {ClassId} count did not rise. "
            + "Escalated to admin ({Outcome}) instead of cancelling (02 §9.2, §10.1)",
            transaction.Id, transaction.ItemAssetId, transaction.ItemClassId, outcome);

        return DeliveryTimeoutDecision.Held;
    }

    /// <summary>
    /// No delivery was established. Whether that may become a cancellation
    /// depends on how much the platform could actually see.
    /// </summary>
    private DeliveryTimeoutDecision Undelivered(
        Transaction transaction, DeliveryVerificationResult result)
    {
        if (SellerProvenToStillHoldTheItem(result))
        {
            _logger.LogInformation(
                "Transaction {TransactionId}: delivery deadline passed and the item is still in "
                + "the seller's inventory — timeout proceeds to cancellation (03 §4.4)",
                transaction.Id);
            return DeliveryTimeoutDecision.Cancel;
        }

        _logger.LogWarning(
            "Transaction {TransactionId}: delivery deadline passed but the platform cannot "
            + "establish whether the item was sent (verdict {Verdict}, seller inventory "
            + "{SellerVisibility}) — held rather than cancelled, since absence of information is "
            + "not a negative finding (08 §2.3). Retried on the next scan.",
            transaction.Id, result.Verdict, result.SellerVisibility);

        return DeliveryTimeoutDecision.Held;
    }

    /// <summary>
    /// The single positive test that authorises a delivery-timeout cancellation:
    /// the seller's inventory was readable this round and the transaction's
    /// asset is still in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It covers <see cref="DeliveryVerdict.NoMovement"/> by construction — that
    /// verdict requires both sides read with nothing observed — and it is what
    /// makes <see cref="DeliveryVerdict.Inconclusive"/> decidable in the one
    /// shape that recurs in production: the buyer's inventory is private (02
    /// §9.2 accepts this and warns them that their own confirmation is then the
    /// only route) while the seller's is readable and still holds the item.
    /// Refusing to cancel there would leave the buyer's money locked with no
    /// exit but an admin.
    /// </para>
    /// <para>
    /// The two shapes it deliberately refuses:
    /// <c>SELLER_ASSET_GONE</c> with an unreadable buyer side — something left
    /// the seller and cancelling would punish a seller who may have delivered —
    /// and an unreadable seller side, where the platform has no observation at
    /// all. Both are held and retried; a Steam outage is meant to be absorbed by
    /// freezing the phase (<c>TimeoutFreezeReasonScopes.STEAM_OUTAGE</c>), not
    /// by cancelling into it.
    /// </para>
    /// </remarks>
    private static bool SellerProvenToStillHoldTheItem(DeliveryVerificationResult result)
        => result.SellerVisibility == InventoryVisibility.Public
            && !result.Evidence.HasFlag(DeliveryEvidence.SELLER_ASSET_GONE);

    /// <summary>
    /// Whether a previous round actually reached
    /// <see cref="DeliveryVerdict.MisdeliverySignature"/> on this transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The capture is the record of the engine's conclusion, and the conclusion
    /// is what the re-entry gate needs — <c>Transaction.DeliveryEvidence</c>
    /// holds observations, and an observation of <c>SELLER_ASSET_GONE</c> is
    /// raised on paths the engine deliberately refuses to call a signature.
    /// </para>
    /// <para>
    /// The verdict is compared by name because that is how the column stores it
    /// (06 §3.5a): these rows outlive the enum's ordering. One indexed seek on
    /// <c>IX_DeliveryEvidenceCaptures_TransactionId</c>, and the answer never
    /// reverts — the escalation it stands for is a dispute row an admin owns.
    /// </para>
    /// </remarks>
    private Task<bool> MisdeliveryAlreadyConcludedAsync(
        Guid transactionId, CancellationToken cancellationToken)
        => _db.Set<DeliveryEvidenceCapture>()
            .AsNoTracking()
            .AnyAsync(
                c => c.TransactionId == transactionId
                     && c.Verdict == MisdeliverySignatureVerdictName,
                cancellationToken);

    private static readonly string MisdeliverySignatureVerdictName =
        nameof(DeliveryVerdict.MisdeliverySignature);
}
