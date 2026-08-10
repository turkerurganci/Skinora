using Skinora.Shared.Enums;
using Skinora.Shared.Exceptions;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Transactions.Tests.Unit.StateMachine;

/// <summary>
/// Unit tests for <see cref="TransactionStateMachine"/> covering the full
/// 05 §4.2 transition table (every state × every trigger, valid + invalid)
/// plus 06 §3.5 required-field guards, RowVersion guard and 05 §4.5
/// emergency hold semantics.
/// </summary>
public class TransactionStateMachineTests
{
    /// <summary>
    /// 05 §4.2 transition table — single source of truth for valid transitions.
    /// Each row: (sourceState, trigger, targetState).
    /// </summary>
    private static readonly (TransactionStatus From, TransactionTrigger Trigger, TransactionStatus To)[] ValidTransitions =
    [
        (TransactionStatus.CREATED, TransactionTrigger.BuyerAccept, TransactionStatus.ACCEPTED),
        (TransactionStatus.CREATED, TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT),
        (TransactionStatus.CREATED, TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER),
        (TransactionStatus.CREATED, TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER),
        (TransactionStatus.CREATED, TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN),

        (TransactionStatus.ACCEPTED, TransactionTrigger.SellerConfirmReady, TransactionStatus.SELLER_CONFIRMED),
        (TransactionStatus.ACCEPTED, TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT),
        (TransactionStatus.ACCEPTED, TransactionTrigger.SellerDecline, TransactionStatus.CANCELLED_SELLER),
        (TransactionStatus.ACCEPTED, TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER),
        (TransactionStatus.ACCEPTED, TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER),
        (TransactionStatus.ACCEPTED, TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN),

        (TransactionStatus.SELLER_CONFIRMED, TransactionTrigger.ConfirmPayment, TransactionStatus.PAYMENT_RECEIVED),
        (TransactionStatus.SELLER_CONFIRMED, TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT),
        (TransactionStatus.SELLER_CONFIRMED, TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER),
        (TransactionStatus.SELLER_CONFIRMED, TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER),
        (TransactionStatus.SELLER_CONFIRMED, TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN),

        // v3.0 — cancel is asymmetric here: the seller may still back out after
        // the buyer has paid, the buyer may not (02 §7).
        (TransactionStatus.PAYMENT_RECEIVED, TransactionTrigger.DeliverItem, TransactionStatus.ITEM_DELIVERED),
        (TransactionStatus.PAYMENT_RECEIVED, TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT),
        (TransactionStatus.PAYMENT_RECEIVED, TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER),
        (TransactionStatus.PAYMENT_RECEIVED, TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN),

        // Settlement window (02 §4.5.1): payout only after the reversal window
        // closed AND the item was re-checked; a reversal unwinds to REFUNDED.
        (TransactionStatus.ITEM_DELIVERED, TransactionTrigger.Complete, TransactionStatus.COMPLETED),
        (TransactionStatus.ITEM_DELIVERED, TransactionTrigger.DeliveryReversed, TransactionStatus.REFUNDED),

        // WP5 — buyer-favor admin dispute resolution unwinds the escrow to REFUNDED.
        (TransactionStatus.SELLER_CONFIRMED, TransactionTrigger.AdminResolveRefund, TransactionStatus.REFUNDED),
        (TransactionStatus.PAYMENT_RECEIVED, TransactionTrigger.AdminResolveRefund, TransactionStatus.REFUNDED),
        (TransactionStatus.ITEM_DELIVERED, TransactionTrigger.AdminResolveRefund, TransactionStatus.REFUNDED),

        (TransactionStatus.FLAGGED, TransactionTrigger.AdminApprove, TransactionStatus.CREATED),
        (TransactionStatus.FLAGGED, TransactionTrigger.AdminReject, TransactionStatus.CANCELLED_ADMIN),
        (TransactionStatus.FLAGGED, TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN),
    ];

    public static IEnumerable<object[]> ValidTransitionData() =>
        ValidTransitions.Select(t => new object[] { t.From, t.Trigger, t.To });

    public static IEnumerable<object[]> InvalidTransitionData()
    {
        var allStates = Enum.GetValues<TransactionStatus>();
        var allTriggers = Enum.GetValues<TransactionTrigger>();
        var validSet = ValidTransitions.Select(t => (t.From, t.Trigger)).ToHashSet();
        foreach (var state in allStates)
        {
            foreach (var trigger in allTriggers)
            {
                if (!validSet.Contains((state, trigger)))
                {
                    yield return [state, trigger];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ValidTransitionData))]
    public void Fire_ValidTransition_MovesToTargetState(
        TransactionStatus from, TransactionTrigger trigger, TransactionStatus to)
    {
        var transaction = NewTransactionWithAllRequiredFields(from);
        var sm = new TransactionStateMachine(transaction);

        FireWithCancelContextIfNeeded(sm, trigger);

        Assert.Equal(to, transaction.Status);
    }

    [Theory]
    [MemberData(nameof(InvalidTransitionData))]
    public void Fire_InvalidTransition_ThrowsDomainExceptionAndDoesNotChangeState(
        TransactionStatus from, TransactionTrigger trigger)
    {
        var transaction = NewTransactionWithAllRequiredFields(from);
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.ThrowsAny<DomainException>(() => FireWithCancelContextIfNeeded(sm, trigger));
        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
        Assert.Equal(from, transaction.Status);
    }

    [Fact]
    public void BuyerAccept_WithoutBuyerId_ThrowsInvalidTransition()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        transaction.BuyerId = null;
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.BuyerAccept));
        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void BuyerAccept_WithoutBuyerRefundAddress_ThrowsInvalidTransition()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        transaction.BuyerRefundAddress = null;
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.BuyerAccept));
        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void BuyerAccept_WithoutBuyerTradeUrl_ThrowsInvalidTransition()
    {
        // T119a — 06 §3.5 puts BuyerTradeUrl in the same NOT-NULL-from-ACCEPTED
        // bracket as BuyerId/BuyerRefundAddress. The column stays nullable in the
        // database (it cannot be filled while CREATED), so this guard is the only
        // place the invariant is enforced.
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        transaction.BuyerTradeUrl = null;
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.BuyerAccept));
        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void SellerConfirmReady_WithoutSellerReadyConfirmedAt_ThrowsInvalidTransition()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.ACCEPTED);
        transaction.SellerReadyConfirmedAt = null;
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.SellerConfirmReady));
        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void DeliverItem_WithoutSufficientEvidence_ThrowsInvalidTransition()
    {
        // 02 §9.2 — SELLER_ASSET_GONE alone is the misdelivery signature, not
        // proof of delivery: the item left the seller but nothing was observed
        // arriving at the buyer.
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.PAYMENT_RECEIVED);
        transaction.DeliveryEvidence = DeliveryEvidence.SELLER_ASSET_GONE;
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.DeliverItem));
        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void DeliverItem_WithoutDeliveryVerifiedAt_ThrowsInvalidTransition()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.PAYMENT_RECEIVED);
        transaction.DeliveryVerifiedAt = null;
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.DeliverItem));
        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void DeliverItem_BuyerConfirmedWithoutDeliveredAssetId_Succeeds()
    {
        // Deliberate: a delivery closed by the buyer's own confirmation may never
        // have read an inventory, so DeliveredBuyerAssetId stays null. The guard
        // must read the evidence flags, not that audit field (02 §9.2).
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.PAYMENT_RECEIVED);
        transaction.DeliveredBuyerAssetId = null;
        transaction.DeliveryEvidence = DeliveryEvidence.BUYER_CONFIRMED;
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.DeliverItem);

        Assert.Equal(TransactionStatus.ITEM_DELIVERED, transaction.Status);
    }

    [Fact]
    public void Complete_WithoutSettlementVerifiedAt_ThrowsInvalidTransition()
    {
        // 02 §4.5.1 — waiting out the reversal window is not clearance; only the
        // check at the end of it is.
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.ITEM_DELIVERED);
        transaction.SettlementVerifiedAt = null;
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.Complete));
        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void Complete_WhenDeliveryReversed_ThrowsInvalidTransition()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.ITEM_DELIVERED);
        transaction.DeliveryReversedAt = DateTime.UtcNow;
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.Complete));
        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void AdminApprove_FromFlaggedWithStaleDeadline_ThrowsInvalidTransition()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.FLAGGED);
        transaction.AcceptDeadline = DateTime.UtcNow.AddHours(1);  // FLAGGED'da olmamalı
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.AdminApprove));
        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void AdminReject_FromFlaggedWithStalePaymentTimeoutJobId_ThrowsInvalidTransition()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.FLAGGED);
        transaction.PaymentTimeoutJobId = "stale-job";
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.AdminReject));
        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void Fire_RowVersionMismatch_ThrowsDomainException()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        transaction.RowVersion = [1, 2, 3, 4];
        var staleVersion = new byte[] { 9, 9, 9, 9 };
        var sm = new TransactionStateMachine(transaction, staleVersion);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.BuyerAccept));
        Assert.Equal(TransactionStateMachine.RowVersionMismatchErrorCode, ex.ErrorCode);
        Assert.Equal(TransactionStatus.CREATED, transaction.Status);
    }

    [Fact]
    public void Fire_RowVersionMatch_TransitionSucceeds()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        transaction.RowVersion = [1, 2, 3, 4];
        var sm = new TransactionStateMachine(transaction, [1, 2, 3, 4]);

        sm.Fire(TransactionTrigger.BuyerAccept);

        Assert.Equal(TransactionStatus.ACCEPTED, transaction.Status);
    }

    [Fact]
    public void Fire_RowVersionNullExpected_GuardSkippedAndTransitionSucceeds()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        transaction.RowVersion = [1, 2, 3, 4];
        var sm = new TransactionStateMachine(transaction);  // expectedRowVersion null

        sm.Fire(TransactionTrigger.BuyerAccept);

        Assert.Equal(TransactionStatus.ACCEPTED, transaction.Status);
    }

    [Fact]
    public void Fire_WhenIsOnHold_ThrowsDomainException()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        transaction.IsOnHold = true;
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.BuyerAccept));
        Assert.Equal(TransactionStateMachine.OnHoldErrorCode, ex.ErrorCode);
        Assert.Equal(TransactionStatus.CREATED, transaction.Status);
    }

    [Fact]
    public void Fire_CancellationWithoutContext_ForUserInitiatedTrigger_ThrowsCancelReasonRequired()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.SellerCancel));
        Assert.Equal(TransactionStateMachine.CancelReasonRequiredErrorCode, ex.ErrorCode);
        Assert.Equal(TransactionStatus.CREATED, transaction.Status);
    }

    [Fact]
    public void Fire_TimeoutTrigger_NoContext_UsesDefaultReasonAndCancelledByTimeout()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.Timeout);

        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, transaction.Status);
        Assert.Equal(CancelledByType.TIMEOUT, transaction.CancelledBy);
        Assert.False(string.IsNullOrEmpty(transaction.CancelReason));
        Assert.NotNull(transaction.CancelledAt);
    }

    [Fact]
    public void Fire_AdminRejectFromFlagged_NoContext_UsesDefaultReasonAndCancelledByAdmin()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.FLAGGED);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.AdminReject);

        Assert.Equal(TransactionStatus.CANCELLED_ADMIN, transaction.Status);
        Assert.Equal(CancelledByType.ADMIN, transaction.CancelledBy);
        Assert.False(string.IsNullOrEmpty(transaction.CancelReason));
        Assert.NotNull(transaction.CancelledAt);
    }

    [Theory]
    [InlineData(TransactionTrigger.SellerCancel, CancelledByType.SELLER)]
    [InlineData(TransactionTrigger.BuyerCancel, CancelledByType.BUYER)]
    [InlineData(TransactionTrigger.AdminCancel, CancelledByType.ADMIN)]
    public void Fire_UserInitiatedCancel_WithContext_StampsCancelledByAndReason(
        TransactionTrigger trigger, CancelledByType expectedBy)
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(trigger, new CancellationContext("Test sebep"));

        Assert.Equal(expectedBy, transaction.CancelledBy);
        Assert.Equal("Test sebep", transaction.CancelReason);
        Assert.NotNull(transaction.CancelledAt);
    }

    [Fact]
    public void OnEntry_AcceptedSetsAcceptedAt()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        transaction.AcceptedAt = null;
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.BuyerAccept);

        Assert.NotNull(transaction.AcceptedAt);
    }

    [Fact]
    public void OnEntry_SellerConfirmedResetsTimeoutWarningSentAt()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.ACCEPTED);
        transaction.TimeoutWarningSentAt = DateTime.UtcNow.AddMinutes(-1);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.SellerConfirmReady);

        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, transaction.Status);
        Assert.Null(transaction.TimeoutWarningSentAt);
    }

    [Fact]
    public void OnExit_FromSellerConfirmedClearsTimeoutWarningJobIdAndSentAt()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.SELLER_CONFIRMED);
        transaction.TimeoutWarningJobId = "warning-job-1";
        transaction.TimeoutWarningSentAt = DateTime.UtcNow.AddMinutes(-1);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.ConfirmPayment);

        Assert.Null(transaction.TimeoutWarningJobId);
        Assert.Null(transaction.TimeoutWarningSentAt);
    }

    // 05 §4.2 guards BOTH cancels out of SELLER_CONFIRMED on
    // `PaymentReceivedAt is null`. Reaching that combination requires a payment
    // recorded without the state advancing — precisely the case where cancelling
    // would strand the buyer's money, so the guard must hold for either party.
    [Theory]
    [InlineData(TransactionTrigger.SellerCancel)]
    [InlineData(TransactionTrigger.BuyerCancel)]
    public void SellerConfirmed_CancelIsRefused_WhenPaymentAlreadyRecorded(TransactionTrigger trigger)
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.SELLER_CONFIRMED);
        transaction.PaymentReceivedAt = DateTime.UtcNow.AddMinutes(-1);
        var sm = new TransactionStateMachine(transaction);

        Assert.False(sm.CanFire(trigger));

        var ex = Assert.Throws<DomainException>(
            () => sm.Fire(trigger, new CancellationContext("Test iptal gerekçesi")));

        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, transaction.Status);
    }

    // 05 §4.2 documents two equivalent seller exits from ACCEPTED:
    // `seller_decline` (refusing the readiness request, 03 §2.3) and
    // `seller_cancel` (the generic cancel endpoint, 07 §7.7). They are separate
    // triggers so TransactionHistory keeps the intent distinguishable, but the
    // outcome must be identical — a divergence would make the same user action
    // score differently depending on which endpoint produced it.
    [Fact]
    public void Accepted_SellerDeclineAndSellerCancel_ProduceIdenticalOutcome()
    {
        var declined = NewTransactionWithAllRequiredFields(TransactionStatus.ACCEPTED);
        var cancelled = NewTransactionWithAllRequiredFields(TransactionStatus.ACCEPTED);

        new TransactionStateMachine(declined)
            .Fire(TransactionTrigger.SellerDecline, new CancellationContext("Vazgeçtim"));
        new TransactionStateMachine(cancelled)
            .Fire(TransactionTrigger.SellerCancel, new CancellationContext("Vazgeçtim"));

        Assert.Equal(TransactionStatus.CANCELLED_SELLER, declined.Status);
        Assert.Equal(declined.Status, cancelled.Status);
        Assert.Equal(declined.CancelledBy, cancelled.CancelledBy);
        Assert.Equal(CancelledByType.SELLER, cancelled.CancelledBy);
        Assert.Equal(declined.CancelReason, cancelled.CancelReason);
        Assert.NotNull(declined.CancelledAt);
        Assert.NotNull(cancelled.CancelledAt);
    }

    // 05 §4.2 `PAYMENT_RECEIVED | buyer_cancel | — Kullanılamaz`. The generated
    // invalid matrix already covers this pair, but the rule is a money-safety
    // decision (02 §7) rather than an incidental gap, so it gets a test that
    // says so by name: once the buyer's funds are in escrow they cannot pull
    // them back unilaterally.
    [Fact]
    public void BuyerCancel_FromPaymentReceived_IsRefused()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.PAYMENT_RECEIVED);
        var sm = new TransactionStateMachine(transaction);

        Assert.False(sm.CanFire(TransactionTrigger.BuyerCancel));

        var ex = Assert.Throws<DomainException>(
            () => sm.Fire(TransactionTrigger.BuyerCancel, new CancellationContext("Vazgeçtim")));

        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, transaction.Status);
        Assert.Null(transaction.CancelledAt);
    }

    // 05 §4.2 `ITEM_DELIVERED | admin_cancel | — Kullanılamaz`. Standard cancel
    // is unavailable once delivery happened: the platform cannot claw the item
    // back, so unwinding is only ever admin_resolve_refund or an exceptional
    // resolution (02 §7). Named for the same reason as the case above.
    [Fact]
    public void AdminCancel_FromItemDelivered_IsRefused()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.ITEM_DELIVERED);
        var sm = new TransactionStateMachine(transaction);

        Assert.False(sm.CanFire(TransactionTrigger.AdminCancel));

        var ex = Assert.Throws<DomainException>(
            () => sm.Fire(TransactionTrigger.AdminCancel, new CancellationContext("Admin iptali")));

        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, transaction.Status);
        // admin_resolve_refund remains the only admin exit from here (05 §4.2).
        Assert.True(sm.CanFire(TransactionTrigger.AdminResolveRefund));
    }

    [Fact]
    public void OnEntry_PaymentReceivedSetsPaymentReceivedAt()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.SELLER_CONFIRMED);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.ConfirmPayment);

        Assert.NotNull(transaction.PaymentReceivedAt);
    }

    [Fact]
    public void OnEntry_ItemDeliveredSetsItemDeliveredAt()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.PAYMENT_RECEIVED);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.DeliverItem);

        Assert.NotNull(transaction.ItemDeliveredAt);
    }

    [Fact]
    public void Fire_DeliveryReversed_FromItemDelivered_MovesToRefunded()
    {
        // 02 §4.5.1 — the settlement re-check found the item gone from the
        // buyer's inventory: the trade was reversed, so the seller is not paid.
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.ITEM_DELIVERED);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.DeliveryReversed);

        Assert.Equal(TransactionStatus.REFUNDED, transaction.Status);
        // CK_Transactions_Cancel requires the full trail on every refund
        // terminal state; the system-produced trigger supplies its own.
        Assert.Equal(CancelledByType.SELLER, transaction.CancelledBy);
        Assert.False(string.IsNullOrEmpty(transaction.CancelReason));
        Assert.NotNull(transaction.CancelledAt);
    }

    [Fact]
    public void OnEntry_CompletedSetsCompletedAt()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.ITEM_DELIVERED);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.Complete);

        Assert.NotNull(transaction.CompletedAt);
    }

    [Fact]
    public void Fire_AdminResolveRefund_FromItemDelivered_StampsRefundedAndCancelFields()
    {
        // WP5 — buyer-favor dispute resolution; REFUNDED reuses the cancellation
        // fields (CancelledBy=ADMIN, reason, CancelledAt) so CK_Transactions_Cancel holds.
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.ITEM_DELIVERED);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.AdminResolveRefund, new CancellationContext("Dispute refund"));

        Assert.Equal(TransactionStatus.REFUNDED, transaction.Status);
        Assert.Equal(CancelledByType.ADMIN, transaction.CancelledBy);
        Assert.Equal("Dispute refund", transaction.CancelReason);
        Assert.NotNull(transaction.CancelledAt);
    }

    [Fact]
    public void ApplyEmergencyHold_StampsAllFields()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.SELLER_CONFIRMED);
        transaction.PaymentDeadline = DateTime.UtcNow.AddMinutes(30);
        var sm = new TransactionStateMachine(transaction);
        var adminId = Guid.NewGuid();

        sm.ApplyEmergencyHold(adminId, "Sanctions match");

        Assert.True(transaction.IsOnHold);
        Assert.NotNull(transaction.EmergencyHoldAt);
        Assert.Equal("Sanctions match", transaction.EmergencyHoldReason);
        Assert.Equal(adminId, transaction.EmergencyHoldByAdminId);
        Assert.Equal((int)TransactionStatus.SELLER_CONFIRMED, transaction.PreviousStatusBeforeHold);
        Assert.Equal(TimeoutFreezeReason.EMERGENCY_HOLD, transaction.TimeoutFreezeReason);
        Assert.NotNull(transaction.TimeoutFrozenAt);
        Assert.NotNull(transaction.TimeoutRemainingSeconds);
        Assert.True(transaction.TimeoutRemainingSeconds > 0);
    }

    // 05 §4.5 + 06 §3.5 state → active-deadline matrix. The delivery phase was
    // added to this switch in v3.0: before the pivot DeliveryDeadline was never
    // armed, so there was no remainder to freeze. Now it is the only clock
    // standing between a paid buyer and a non-delivering seller — holding a
    // PAYMENT_RECEIVED transaction without capturing it would resume the
    // delivery window from scratch.
    [Fact]
    public void ApplyEmergencyHold_OnPaymentReceived_CapturesDeliveryDeadlineRemainder()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.PAYMENT_RECEIVED);
        transaction.DeliveryDeadline = DateTime.UtcNow.AddHours(6);
        // The payment-phase deadline is stale by now; picking it here would
        // freeze the wrong clock, so it is seeded deliberately.
        transaction.PaymentDeadline = DateTime.UtcNow.AddMinutes(-30);
        var sm = new TransactionStateMachine(transaction);

        sm.ApplyEmergencyHold(Guid.NewGuid(), "Sanctions match");

        Assert.True(transaction.IsOnHold);
        Assert.Equal((int)TransactionStatus.PAYMENT_RECEIVED, transaction.PreviousStatusBeforeHold);
        Assert.NotNull(transaction.TimeoutRemainingSeconds);
        // Six hours minus the elapsed wall-clock inside the call.
        Assert.InRange(transaction.TimeoutRemainingSeconds!.Value, 6 * 3600 - 60, 6 * 3600);
    }

    [Fact]
    public void ApplyEmergencyHold_OnPaymentReceived_PastDeliveryDeadline_ClampsToZero()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.PAYMENT_RECEIVED);
        transaction.DeliveryDeadline = DateTime.UtcNow.AddMinutes(-5);
        var sm = new TransactionStateMachine(transaction);

        sm.ApplyEmergencyHold(Guid.NewGuid(), "Investigation");

        // CK_Transactions_FreezeActive (06 §3.5) rejects a negative remainder.
        Assert.Equal(0, transaction.TimeoutRemainingSeconds);
    }

    [Fact]
    public void ApplyEmergencyHold_OnAcceptedState_DoesNotSetTimeoutRemainingSeconds()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.ACCEPTED);
        var sm = new TransactionStateMachine(transaction);

        sm.ApplyEmergencyHold(Guid.NewGuid(), "Investigation");

        Assert.True(transaction.IsOnHold);
        Assert.Null(transaction.TimeoutRemainingSeconds);
    }

    [Fact]
    public void ApplyEmergencyHold_AlreadyOnHold_ThrowsDomainException()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        transaction.IsOnHold = true;
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.ApplyEmergencyHold(Guid.NewGuid(), "Sebep"));
        Assert.Equal(TransactionStateMachine.AlreadyOnHoldErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void ApplyEmergencyHold_EmptyReason_ThrowsDomainException()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.ApplyEmergencyHold(Guid.NewGuid(), ""));
        Assert.Equal(TransactionStateMachine.EmergencyHoldReasonRequiredErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void ApplyEmergencyHold_RowVersionMismatch_ThrowsDomainException()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        transaction.RowVersion = [1, 2, 3, 4];
        var sm = new TransactionStateMachine(transaction, [9, 9, 9, 9]);

        var ex = Assert.Throws<DomainException>(() => sm.ApplyEmergencyHold(Guid.NewGuid(), "Sebep"));
        Assert.Equal(TransactionStateMachine.RowVersionMismatchErrorCode, ex.ErrorCode);
        Assert.False(transaction.IsOnHold);
    }

    [Fact]
    public void ReleaseEmergencyHold_ClearsHoldFlagAndFreezeFields()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.SELLER_CONFIRMED);
        transaction.PaymentDeadline = DateTime.UtcNow.AddMinutes(30);
        var sm = new TransactionStateMachine(transaction);
        sm.ApplyEmergencyHold(Guid.NewGuid(), "Sebep");

        sm.ReleaseEmergencyHold();

        Assert.False(transaction.IsOnHold);
        Assert.Null(transaction.TimeoutFreezeReason);
        Assert.Null(transaction.TimeoutFrozenAt);
        // Audit alanları korunur
        Assert.NotNull(transaction.EmergencyHoldAt);
        Assert.NotNull(transaction.EmergencyHoldReason);
        Assert.NotNull(transaction.PreviousStatusBeforeHold);
        // 05 §4.4 "Otorite": the remainder is what the reschedule reads, so
        // release must not clear it — T50 ResumeAsync consumes it afterwards.
        Assert.NotNull(transaction.TimeoutRemainingSeconds);
    }

    [Fact]
    public void ReleaseEmergencyHold_NotOnHold_ThrowsDomainException()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.ReleaseEmergencyHold());
        Assert.Equal(TransactionStateMachine.NotOnHoldErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void PermittedTriggers_ReportsValidTriggersForCurrentState()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
        var sm = new TransactionStateMachine(transaction);

        var permitted = sm.PermittedTriggers.ToHashSet();

        Assert.Contains(TransactionTrigger.BuyerAccept, permitted);
        Assert.Contains(TransactionTrigger.Timeout, permitted);
        Assert.Contains(TransactionTrigger.AdminCancel, permitted);
        Assert.DoesNotContain(TransactionTrigger.SellerConfirmReady, permitted);
        Assert.DoesNotContain(TransactionTrigger.Complete, permitted);
    }

    [Fact]
    public void Constructor_NullTransaction_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TransactionStateMachine(null!));
    }

    // -- Helpers --

    private static Transaction NewTransactionWithAllRequiredFields(TransactionStatus status)
    {
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = Guid.NewGuid(),
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000099",
            ItemAssetId = "100001",
            ItemClassId = "200002",
            ItemName = "Test Skin",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = "TX1234567890",
            PaymentTimeoutMinutes = 30,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        // FLAGGED state invariant (06 §3.5 + 03 §7): tüm milestone field'lar ve
        // deadline/Hangfire job ID'leri NULL kalır. Diğer state'lerde forward
        // geçişin guard'ı için tüm caller-set alanlar seed edilir; OnEntry
        // timestamp'leri state machine tarafından doldurulur.
        if (status != TransactionStatus.FLAGGED)
        {
            transaction.BuyerId = Guid.NewGuid();
            transaction.BuyerRefundAddress = "TX9876543210";
            transaction.BuyerTradeUrl = "https://steamcommunity.com/tradeoffer/new/?partner=1&token=abc";
            transaction.SellerReadyConfirmedAt = DateTime.UtcNow;
            transaction.DeliveredBuyerAssetId = "DEL-123";

            // Forward-path guards read these, so seed the "everything observed"
            // shape; the individual negative cases null them out one at a time.
            transaction.DeliveryEvidence =
                DeliveryEvidence.SELLER_ASSET_GONE | DeliveryEvidence.INVENTORY_DELTA;
            transaction.DeliveryVerifiedAt = DateTime.UtcNow;
            transaction.SettlementVerifiedAt = DateTime.UtcNow;
        }

        // Cumulative milestone timestamps per current status (06 §3.5 matrix).
        if (StatusRequiresAccepted(status)) transaction.AcceptedAt = DateTime.UtcNow;
        if (StatusRequiresPaymentReceivedAt(status)) transaction.PaymentReceivedAt = DateTime.UtcNow;
        if (StatusRequiresItemDelivered(status)) transaction.ItemDeliveredAt = DateTime.UtcNow;
        if (status == TransactionStatus.COMPLETED) transaction.CompletedAt = DateTime.UtcNow;
        if (StatusIsCancelled(status))
        {
            transaction.CancelledBy = CancelledByType.SELLER;
            transaction.CancelReason = "seed";
            transaction.CancelledAt = DateTime.UtcNow;
        }

        return transaction;
    }

    private static bool StatusRequiresAccepted(TransactionStatus s) => s is
        TransactionStatus.ACCEPTED
        or TransactionStatus.SELLER_CONFIRMED
        or TransactionStatus.PAYMENT_RECEIVED
        or TransactionStatus.ITEM_DELIVERED
        or TransactionStatus.COMPLETED;

    private static bool StatusRequiresItemDelivered(TransactionStatus s) => s is
        TransactionStatus.ITEM_DELIVERED
        or TransactionStatus.COMPLETED;

    private static bool StatusRequiresPaymentReceivedAt(TransactionStatus s) => s is
        TransactionStatus.PAYMENT_RECEIVED
        or TransactionStatus.ITEM_DELIVERED
        or TransactionStatus.COMPLETED;

    private static bool StatusIsCancelled(TransactionStatus s) => s is
        TransactionStatus.CANCELLED_TIMEOUT
        or TransactionStatus.CANCELLED_SELLER
        or TransactionStatus.CANCELLED_BUYER
        or TransactionStatus.CANCELLED_ADMIN
        or TransactionStatus.REFUNDED;

    private static void FireWithCancelContextIfNeeded(TransactionStateMachine sm, TransactionTrigger trigger)
    {
        if (IsUserInitiatedCancel(trigger))
        {
            sm.Fire(trigger, new CancellationContext("test"));
        }
        else
        {
            sm.Fire(trigger);
        }
    }

    private static bool IsUserInitiatedCancel(TransactionTrigger t) => t is
        TransactionTrigger.SellerCancel
        or TransactionTrigger.BuyerCancel
        or TransactionTrigger.AdminCancel
        or TransactionTrigger.SellerDecline
        // WP5 — reason-required (no default), stamped CancelledBy=ADMIN.
        or TransactionTrigger.AdminResolveRefund;
}
