using Microsoft.Extensions.Logging;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Application.Settlement;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// Fires <c>DeliverItem</c> on inventory evidence that has cleared the launch
/// gate (02 §9.2, DEPLOY_RUNBOOK §H) and records the round. Shared by the two
/// platform-side callers that can conclude a delivery from inventories: the
/// timeout round (T127) and the pre-deadline delivery poll
/// (P2P-DeliveryPollingJob).
/// </summary>
/// <remarks>
/// Extracted from <see cref="DeliveryTimeoutRound"/> unchanged, so the two
/// callers cannot drift apart on the stamp order, the rollback discipline or
/// what the transition publishes. The caller owns <c>SaveChanges</c>.
/// </remarks>
internal sealed class InventoryDeliveryTransition
{
    private readonly AppDbContext _db;
    private readonly ISettlementSettingsProvider _settlementSettings;
    private readonly IOutboxService _outbox;
    private readonly ILogger _logger;

    public InventoryDeliveryTransition(
        AppDbContext db,
        ISettlementSettingsProvider settlementSettings,
        IOutboxService outbox,
        ILogger logger)
    {
        _db = db;
        _settlementSettings = settlementSettings;
        _outbox = outbox;
        _logger = logger;
    }

    /// <summary>
    /// <c>true</c> when the transaction moved to <c>ITEM_DELIVERED</c>;
    /// <c>false</c> when the state machine refused, in which case every field
    /// this method stamped has been rolled back.
    /// </summary>
    public async Task<bool> TryDeliverAsync(
        Transaction transaction,
        DeliveryVerificationResult result,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Captured so a refused trigger can be rolled back field by field. This
        // matters here in a way it does not in the confirm-receipt endpoint
        // (T126): that caller owns its SaveChanges and can simply return without
        // saving, while the callers here share a unit of work with the rest of a
        // scan — a half-stamped transaction would be committed by somebody
        // else's cancellation. And the half that would survive is precisely
        // DeliveryVerifiedAt, the field holding the launch gate shut.
        var previousVerifiedAt = transaction.DeliveryVerifiedAt;
        var previousDeliveredAssetId = transaction.DeliveredBuyerAssetId;
        var previousPayoutEligibleAt = transaction.PayoutEligibleAt;
        var previousStatus = transaction.Status;

        // 02 §9.2 invariant: stamped BEFORE the guard runs (HasDeliveryEvidence
        // reads IsSufficientForDelivery() && DeliveryVerifiedAt.HasValue).
        transaction.DeliveryVerifiedAt = nowUtc;

        // T129 — 02 §4.5.1. Same ordering rule and the same rollback discipline
        // as the stamp above: the ITEM_DELIVERED guard now also demands the
        // settlement window, and a half-stamped row committed by somebody else's
        // unit of work would be a transaction whose payout clock was opened
        // without a delivery.
        var settlement = await _settlementSettings.GetAsync(cancellationToken);
        SettlementWindowStamper.Stamp(transaction, nowUtc, settlement.SettlementDays);

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
            transaction.PayoutEligibleAt = previousPayoutEligibleAt;

            _logger.LogError(ex,
                "Transaction {TransactionId}: inventory evidence proved delivery but "
                + "DeliverItem was refused ({ErrorCode}) — the stamp was rolled back and the "
                + "transaction stays in {Status}",
                transaction.Id, ex.ErrorCode, transaction.Status);
            return false;
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

        return true;
    }
}
