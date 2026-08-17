using Microsoft.Extensions.Logging;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Application.Settlement;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// T130 — default <see cref="IDeliveryDisputeRound"/>. Turns one
/// <see cref="DeliveryVerdict"/> into the 03 §6.2 outcome the buyer is answered
/// with.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="DeliveryTimeoutRound"/>, and deliberately a
/// separate class: the two share a verification engine but not a decision. A
/// timeout may cancel; a dispute may not — the buyer asked a question, and
/// answering it by cancelling their transaction would be a side effect nobody
/// requested. Every arm here either advances the transaction or leaves it
/// exactly where it was.
/// </para>
/// </remarks>
public sealed class DeliveryDisputeRound : IDeliveryDisputeRound
{
    private readonly AppDbContext _db;
    private readonly IDeliveryVerificationService _verification;
    private readonly ISettlementSettingsProvider _settlementSettings;
    private readonly IOutboxService _outbox;
    private readonly ILogger<DeliveryDisputeRound> _logger;
    private readonly TimeProvider _clock;

    public DeliveryDisputeRound(
        AppDbContext db,
        IDeliveryVerificationService verification,
        ISettlementSettingsProvider settlementSettings,
        IOutboxService outbox,
        ILogger<DeliveryDisputeRound> logger,
        TimeProvider clock)
    {
        _db = db;
        _verification = verification;
        _settlementSettings = settlementSettings;
        _outbox = outbox;
        _logger = logger;
        _clock = clock;
    }

    public async Task<DeliveryDisputeOutcome> RunAsync(
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        // 02 §10.1 says the evidence rules run "taze olarak" on this path, and
        // says it about the dispute path specifically: the buyer is claiming
        // something the platform's last poll did not see, so serving them a
        // 120-second-old inventory answers a question they did not ask.
        var result = await _verification.VerifyAsync(
            transaction, InventoryReadFreshness.Fresh, cancellationToken);

        // Recorded on every arm, exactly as in the timeout round: evidence flags
        // are observations, and this round may be the first time anyone looked.
        // The timestamp is the field that must wait — see DeliverAsync.
        transaction.DeliveryEvidence = result.Evidence;

        return result.Verdict switch
        {
            DeliveryVerdict.Delivered =>
                await DeliverAsync(transaction, result, nowUtc, cancellationToken),

            DeliveryVerdict.InventoryEvidencePendingReview =>
                HoldForReview(transaction, result, nowUtc),

            DeliveryVerdict.MisdeliverySignature =>
                Escalate(transaction, result, nowUtc),

            DeliveryVerdict.NoMovement => DeliveryDisputeOutcome.NotSent,

            _ => Unreadable(transaction, result),
        };
    }

    /// <summary>
    /// 03 §6.2 Sonuç A — "İşlem ITEM_DELIVERED durumuna geçer, dispute anında
    /// kapanır".
    /// </summary>
    private async Task<DeliveryDisputeOutcome> DeliverAsync(
        Transaction transaction,
        DeliveryVerificationResult result,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // A DELIVERY dispute is openable in ITEM_DELIVERED too (02 §10.1): the
        // buyer may dispute a delivery the platform already concluded. There is
        // nothing left to transition, and firing the trigger would throw — but
        // the answer to the buyer is unchanged, since the evidence still says
        // the item arrived.
        if (transaction.Status != TransactionStatus.PAYMENT_RECEIVED)
        {
            _logger.LogInformation(
                "Transaction {TransactionId}: delivery dispute round proved delivery on a "
                + "transaction already in {Status} — answered from the evidence, no transition",
                transaction.Id, transaction.Status);
            return DeliveryDisputeOutcome.Delivered;
        }

        // Captured so a refused trigger rolls back field by field. The dispute
        // service owns the SaveChanges and will commit the dispute row whatever
        // this arm concludes, so a half-stamped transaction would be persisted
        // by somebody else's unit of work — and the half that survives is
        // DeliveryVerifiedAt, the field holding the launch gate shut.
        var previousVerifiedAt = transaction.DeliveryVerifiedAt;
        var previousDeliveredAssetId = transaction.DeliveredBuyerAssetId;
        var previousPayoutEligibleAt = transaction.PayoutEligibleAt;
        var previousStatus = transaction.Status;

        // 02 §9.2 invariant: stamped BEFORE the guard runs (HasDeliveryEvidence
        // reads IsSufficientForDelivery() && DeliveryVerifiedAt.HasValue).
        transaction.DeliveryVerifiedAt = nowUtc;

        // T129 — 02 §4.5.1. The ITEM_DELIVERED guard also demands the settlement
        // window; same ordering rule and same rollback discipline as above.
        var settlement = await _settlementSettings.GetAsync(cancellationToken);
        SettlementWindowStamper.Stamp(transaction, nowUtc, settlement.SettlementDays);

        // 06 §8.4 — best-effort audit material, never a guard, and only ever
        // written once: a later round's candidate must not overwrite an id an
        // earlier observation already named.
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
            transaction.PayoutEligibleAt = previousPayoutEligibleAt;

            // An emergency hold is the reachable shape here (05 §4.5 freezes
            // every trigger). Answering "delivered" would tell the buyer their
            // dispute is settled while the transaction sits frozen, so the
            // dispute stays open and escalatable instead — the evidence is real,
            // and a human is exactly what a held transaction needs.
            _logger.LogError(ex,
                "Transaction {TransactionId}: delivery dispute round proved delivery but "
                + "DeliverItem was refused ({ErrorCode}) — the stamp was rolled back and the "
                + "dispute stays open for review",
                transaction.Id, ex.ErrorCode);

            DeliveryEvidenceCaptureRecorder.Record(_db, transaction, result, nowUtc);
            return DeliveryDisputeOutcome.PendingReview;
        }

        // WP15 — audit-trail row (06 §3.6). SYSTEM actor: the buyer opened a
        // dispute, they did not confirm receipt. The conclusion is the
        // platform's own inference, exactly as on the timeout path.
        TransactionHistoryRecorder.Record(
            _db, transaction, previousStatus, TransactionTrigger.DeliverItem,
            ActorType.SYSTEM, SeedConstants.SystemUserId, nowUtc);

        DeliveryEvidenceCaptureRecorder.Record(_db, transaction, result, nowUtc);

        // Feeds the WP9 realtime relay — 03 §3.5 step 9 gives ITEM_DELIVERED no
        // inbox/email type of its own. Published into the same unit of work as
        // the transition so no client is told about a delivery that rolled back.
        await _outbox.PublishAsync(
            new TransactionStatusChangedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                FromStatus: previousStatus,
                ToStatus: transaction.Status,
                OccurredAt: nowUtc),
            cancellationToken);

        _logger.LogInformation(
            "Transaction {TransactionId}: delivery dispute round proved delivery — "
            + "PAYMENT_RECEIVED → ITEM_DELIVERED, evidence {Evidence} (03 §6.2 Sonuç A)",
            transaction.Id, transaction.DeliveryEvidence);

        return DeliveryDisputeOutcome.Delivered;
    }

    /// <summary>
    /// 03 §6.2 Sonuç E — the launch-gate arm (DEPLOY_RUNBOOK §H).
    /// </summary>
    /// <remarks>
    /// <c>DeliveryVerifiedAt</c> is deliberately NOT stamped: the state-machine
    /// guard knows nothing about the gate, so stamping it here would open the
    /// gate silently. What T130 changes is the other half — the buyer keeps a
    /// live dispute and an escalation route, instead of being told "delivered"
    /// while the gate holds their counterparty's payout.
    /// </remarks>
    private DeliveryDisputeOutcome HoldForReview(
        Transaction transaction,
        DeliveryVerificationResult result,
        DateTime nowUtc)
    {
        DeliveryEvidenceCaptureRecorder.Record(_db, transaction, result, nowUtc);

        _logger.LogWarning(
            "Transaction {TransactionId}: delivery dispute found sufficient inventory evidence "
            + "but the launch gate is closed — the dispute stays OPEN and escalatable rather "
            + "than closing as delivered (DEPLOY_RUNBOOK §H.2)",
            transaction.Id);

        return DeliveryDisputeOutcome.PendingReview;
    }

    /// <summary>
    /// 03 §6.2 Sonuç C — the item left the seller and never arrived.
    /// </summary>
    private DeliveryDisputeOutcome Escalate(
        Transaction transaction,
        DeliveryVerificationResult result,
        DateTime nowUtc)
    {
        DeliveryEvidenceCaptureRecorder.Record(_db, transaction, result, nowUtc);

        _logger.LogWarning(
            "Transaction {TransactionId}: delivery dispute hit the misdelivery signature — "
            + "seller asset {AssetId} is gone but the buyer's {ClassId} count did not rise. "
            + "The dispute opens ESCALATED (02 §10.1, 03 §6.2 Sonuç C)",
            transaction.Id, transaction.ItemAssetId, transaction.ItemClassId);

        return DeliveryDisputeOutcome.MisdeliverySignature;
    }

    /// <summary>
    /// 03 §6.2 Sonuç D — at least one side could not be read.
    /// </summary>
    private DeliveryDisputeOutcome Unreadable(
        Transaction transaction, DeliveryVerificationResult result)
    {
        _logger.LogInformation(
            "Transaction {TransactionId}: delivery dispute could not establish delivery either "
            + "way (verdict {Verdict}, seller {SellerVisibility}, buyer {BuyerVisibility}) — "
            + "absence of information is not a negative finding (08 §2.3)",
            transaction.Id, result.Verdict, result.SellerVisibility, result.BuyerVisibility);

        return DeliveryDisputeOutcome.Unreadable;
    }
}
