using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Settlement;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// T126 — 07 §7.6b / 03 §3.5 implementation. Drives
/// <c>PAYMENT_RECEIVED → ITEM_DELIVERED</c> on the buyer's own confirmation.
/// Evidence merge, verification round, history row and outbox publish land in a
/// single <see cref="DbContext.SaveChangesAsync"/> so the transition is atomic
/// with the <c>TransactionStatusChangedEvent</c> the realtime relay rides on.
/// </summary>
public sealed class DeliveryConfirmationService : IDeliveryConfirmationService
{
    private readonly AppDbContext _db;
    private readonly IDeliveryVerificationService _verification;
    private readonly ISettlementSettingsProvider _settlementSettings;
    private readonly IOutboxService _outbox;
    private readonly ILogger<DeliveryConfirmationService> _logger;
    private readonly TimeProvider _clock;

    public DeliveryConfirmationService(
        AppDbContext db,
        IDeliveryVerificationService verification,
        ISettlementSettingsProvider settlementSettings,
        IOutboxService outbox,
        ILogger<DeliveryConfirmationService> logger,
        TimeProvider clock)
    {
        _db = db;
        _verification = verification;
        _settlementSettings = settlementSettings;
        _outbox = outbox;
        _logger = logger;
        _clock = clock;
    }

    public async Task<ConfirmReceiptOutcome> ConfirmReceiptAsync(
        Guid buyerId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        // ---------- Stage 1: load ----------
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted, cancellationToken);
        if (transaction is null)
            return Failure(ConfirmReceiptStatus.NotFound,
                TransactionErrorCodes.TransactionNotFound,
                "Transaction not found.");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        // ---------- Stage 2: party guard (buyer only — 07 §7.6b) ----------
        // Ahead of the state guard so a stranger probing arbitrary ids learns
        // nothing about which state a transaction is in. The seller is refused
        // here too: their claim to have sent the item is not evidence under
        // 02 §9.2, which is the whole reason the inventory path exists.
        //
        // No suspension guard, unlike cancel (T105a): confirming receipt runs
        // against the caller's own interest, so 02 §14.0's "a suspended user may
        // not move funds on their own initiative" does not reach it. Refusing
        // would instead strand a seller who did deliver — with the launch gate
        // closed, T127's timeout round leaves such a transaction parked in
        // PAYMENT_RECEIVED rather than paying out.
        if (transaction.BuyerId != buyerId)
            return Failure(ConfirmReceiptStatus.NotAParty,
                TransactionErrorCodes.NotAParty,
                "Only the buyer can confirm receipt (07 §7.6b).");

        // ---------- Stage 3: idempotency (07 §7.6b) ----------
        // A repeat on an already-delivered transaction returns 200 and the
        // current state. Deliberately scoped to ITEM_DELIVERED alone: COMPLETED
        // and REFUNDED also sit after a delivery, but they describe a different
        // fact (paid out / refunded), and answering 200 there would read as this
        // call having confirmed something.
        if (transaction.Status == TransactionStatus.ITEM_DELIVERED)
            return new ConfirmReceiptOutcome(
                ConfirmReceiptStatus.AlreadyDelivered,
                BuildResponse(transaction, nowUtc),
                ErrorCode: null,
                ErrorMessage: null);

        // ---------- Stage 4: state guard (PAYMENT_RECEIVED only) ----------
        if (transaction.Status != TransactionStatus.PAYMENT_RECEIVED)
            return Failure(ConfirmReceiptStatus.InvalidStateTransition,
                TransactionErrorCodes.InvalidStateTransition,
                $"Cannot confirm receipt in state {transaction.Status} (05 §4.2).");

        // An emergency hold freezes every trigger (05 §4.5). The state machine
        // would reject this at Stage 8 with the same code; the early exit keeps
        // the evidence merge below from running against a frozen transaction.
        if (transaction.IsOnHold)
            return Failure(ConfirmReceiptStatus.InvalidStateTransition,
                TransactionStateMachine.OnHoldErrorCode,
                "Transaction is under emergency hold (05 §4.5).");

        // ---------- Stage 5: record the confirmation, THEN verify ----------
        // Order is load-bearing. 02 §9.2 requires the evidence rules to run when
        // the buyer confirms, and this service delegates them rather than
        // restating them — but the flag goes on first so the engine sees a
        // transaction whose delivery is already proven. It then short-circuits:
        // no Steam round-trips, verdict Delivered, AutoReleaseGated false.
        //
        // The reverse order would read both inventories and could return
        // AutoReleaseGated = true (inventory evidence complete, launch gate
        // closed) on a transaction the buyer has just confirmed. Honouring the
        // F3 invariant there would refuse a confirmation the gate was never
        // meant to touch, and exempting it would turn a mechanical money-safety
        // rule into a conditional one. Merging first keeps both intact.
        transaction.DeliveryEvidence |= DeliveryEvidence.BUYER_CONFIRMED;

        // Fresh rather than Cached: this round decides a money movement, so the
        // freshness contract on the port is honoured even though the
        // short-circuit means no read is issued today (02 §10.1).
        var result = await _verification.VerifyAsync(
            transaction, InventoryReadFreshness.Fresh, cancellationToken);

        transaction.DeliveryEvidence = result.Evidence;

        // ---------- Stage 6: launch-gate invariant (T125 finding F3) ----------
        // DeliveryVerifiedAt is the field that actually holds the gate shut:
        // HasDeliveryEvidence() guards DeliverItem on IsSufficientForDelivery()
        // && DeliveryVerifiedAt.HasValue and knows nothing about the gate, so a
        // caller that persists the evidence AND stamps the timestamp on a gated
        // round opens it silently (DEPLOY_RUNBOOK §H).
        //
        // Structurally unreachable from here — Decide() returns Delivered
        // whenever the evidence carries BUYER_CONFIRMED, which Stage 5 just
        // guaranteed. It is a real branch rather than a comment because the
        // assumption lives in another class: if the engine's gate rules ever
        // widen, this endpoint must refuse to deliver, not quietly release
        // money. Nothing is written on that path (no SaveChanges below), so a
        // retry sees exactly the state it started from.
        if (result.AutoReleaseGated)
        {
            _logger.LogError(
                "Transaction {TransactionId}: confirm-receipt got a gated verdict {Verdict} "
                + "even though BUYER_CONFIRMED was recorded — delivery refused rather than "
                + "stamping DeliveryVerifiedAt (DEPLOY_RUNBOOK §H, T125 F3)",
                transaction.Id, result.Verdict);
            return Failure(ConfirmReceiptStatus.InvalidStateTransition,
                TransactionErrorCodes.InvalidStateTransition,
                "Delivery is held for review and cannot be confirmed (02 §9.2 launch gate).");
        }

        // ---------- Stage 7: stamp the evidence timestamps ----------
        // 02 §9.2 invariant: DeliveryVerifiedAt must be set BEFORE the
        // state-machine guard fires (HasDeliveryEvidence).
        transaction.DeliveryVerifiedAt = nowUtc;

        // 06 §3.5 — "Alıcının 'teslim aldım' onayını verdiği an". The two
        // columns carry the same value here and only here: DeliveryVerifiedAt
        // says when delivery was established by ANY route, this one says the
        // route was the buyer's own word. T127's timeout round and T130's
        // dispute round will stamp the former without ever touching this, which
        // is what makes the pair worth keeping apart — the evidence flags say
        // WHAT was concluded, these say WHEN each conclusion was reached.
        // Stamped here rather than beside the Stage 5 flag so the gated branch
        // above keeps writing nothing at all.
        transaction.BuyerConfirmedReceiptAt = nowUtc;

        // 06 §8.4 — best-effort audit material, never a guard. Null on this path
        // today (the short-circuit reads no inventory), and only ever written
        // once: a later round's candidate must not overwrite an id an earlier
        // observation already named.
        if (result.CandidateDeliveredAssetId is { } candidate
            && string.IsNullOrEmpty(transaction.DeliveredBuyerAssetId))
        {
            transaction.DeliveredBuyerAssetId = candidate;
        }

        // ---------- Stage 7b: open the settlement window (T129 — 02 §4.5.1) ----------
        // The buyer's confirmation proves the item arrived; it does not prove the
        // trade will stand. Steam keeps it reversible for 7 days and the seller
        // can start that reversal without Steam Support, so this is exactly the
        // path that must not pay on delivery alone. Stamped before the trigger:
        // the ITEM_DELIVERED guard refuses the transition without the column.
        var settlement = await _settlementSettings.GetAsync(cancellationToken);
        SettlementWindowStamper.Stamp(transaction, nowUtc, settlement.SettlementDays);

        // ---------- Stage 8: transition ----------
        var previousStatus = transaction.Status;
        var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
        try
        {
            machine.Fire(TransactionTrigger.DeliverItem);
        }
        catch (DomainException ex)
        {
            return Failure(ConfirmReceiptStatus.InvalidStateTransition,
                ex.ErrorCode,
                ex.Message);
        }

        // ---------- Stage 9: audit rows ----------
        // WP15 — history row (06 §3.6). The buyer is the actor (USER).
        TransactionHistoryRecorder.Record(
            _db, transaction, previousStatus, TransactionTrigger.DeliverItem,
            ActorType.USER, buyerId, nowUtc);

        // Called unconditionally per the T125 contract: it no-ops when the round
        // produced no capture, which is the case here (a buyer-confirmed round
        // observes nothing). Wired anyway so this caller stays correct if the
        // engine's capture rules change.
        DeliveryEvidenceCaptureRecorder.Record(_db, transaction, result, nowUtc);

        // No CancelTimeoutJobsAsync: the per-transaction payment/warning jobs
        // were already deleted when ConfirmPayment fired, and the delivery
        // window is a scanner-driven column (05 §4.4) whose query filters on
        // PAYMENT_RECEIVED — leaving PAYMENT_RECEIVED removes this transaction
        // from it. DeliveryDeadline is kept as the historical record of the
        // window the seller actually had.

        // ---------- Stage 10: outbox publish ----------
        // Feeds the WP9 realtime relay, which is the whole delivery-side
        // notification surface: 03 §3.5 step 9 is explicit that ITEM_DELIVERED
        // has no inbox/email type of its own (06 §2.13 does not define one), and
        // the status-changed consumer ignores every ToStatus but SELLER_CONFIRMED
        // and PAYMENT_RECEIVED. Published inside the same SaveChanges as the
        // transition so no client is told about a delivery that rolled back.
        await _outbox.PublishAsync(
            new TransactionStatusChangedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                FromStatus: previousStatus,
                ToStatus: transaction.Status,
                OccurredAt: nowUtc),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Transaction {TransactionId} confirmed received by buyer {BuyerId} — "
            + "PAYMENT_RECEIVED → ITEM_DELIVERED, evidence {Evidence} (02 §9.2)",
            transaction.Id, buyerId, transaction.DeliveryEvidence);

        return new ConfirmReceiptOutcome(
            ConfirmReceiptStatus.Confirmed,
            BuildResponse(transaction, nowUtc),
            ErrorCode: null,
            ErrorMessage: null);
    }

    private static ConfirmReceiptResponse BuildResponse(Transaction transaction, DateTime nowUtc) =>
        new(Status: transaction.Status,
            // ITEM_DELIVERED is only reachable through DeliverItem, whose guard
            // requires DeliveryVerifiedAt and whose OnEntry stamps
            // ItemDeliveredAt, so both fallbacks are unreachable. They exist
            // because the alternative on a broken invariant is a 500 on an
            // idempotent repeat that has nothing left to do.
            DeliveryVerifiedAt: transaction.DeliveryVerifiedAt
                ?? transaction.ItemDeliveredAt
                ?? nowUtc,
            Evidence: DescribeEvidence(transaction.DeliveryEvidence));

    /// <summary>
    /// Expand the <c>[Flags]</c> evidence value into its set member names
    /// (07 §7.6b <c>evidence</c> array). <c>NONE</c> is the absence of evidence,
    /// not a member, so it never appears — an empty array says the same thing
    /// without inviting a client to treat "NONE" as a value.
    /// </summary>
    private static IReadOnlyList<string> DescribeEvidence(DeliveryEvidence evidence) =>
    [
        .. Enum.GetValues<DeliveryEvidence>()
            .Where(flag => flag != DeliveryEvidence.NONE && evidence.HasFlag(flag))
            .Select(flag => flag.ToString())
    ];

    private static ConfirmReceiptOutcome Failure(
        ConfirmReceiptStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);
}
