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

        (TransactionStatus.ACCEPTED, TransactionTrigger.SendTradeOfferToSeller, TransactionStatus.TRADE_OFFER_SENT_TO_SELLER),
        (TransactionStatus.ACCEPTED, TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT),
        (TransactionStatus.ACCEPTED, TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER),
        (TransactionStatus.ACCEPTED, TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER),
        (TransactionStatus.ACCEPTED, TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN),

        (TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, TransactionTrigger.EscrowItem, TransactionStatus.ITEM_ESCROWED),
        (TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT),
        (TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, TransactionTrigger.SellerDecline, TransactionStatus.CANCELLED_SELLER),
        (TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER),
        (TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN),

        (TransactionStatus.ITEM_ESCROWED, TransactionTrigger.ConfirmPayment, TransactionStatus.PAYMENT_RECEIVED),
        (TransactionStatus.ITEM_ESCROWED, TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT),
        (TransactionStatus.ITEM_ESCROWED, TransactionTrigger.SellerCancel, TransactionStatus.CANCELLED_SELLER),
        (TransactionStatus.ITEM_ESCROWED, TransactionTrigger.BuyerCancel, TransactionStatus.CANCELLED_BUYER),
        (TransactionStatus.ITEM_ESCROWED, TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN),

        (TransactionStatus.PAYMENT_RECEIVED, TransactionTrigger.SendTradeOfferToBuyer, TransactionStatus.TRADE_OFFER_SENT_TO_BUYER),
        (TransactionStatus.PAYMENT_RECEIVED, TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN),

        (TransactionStatus.TRADE_OFFER_SENT_TO_BUYER, TransactionTrigger.DeliverItem, TransactionStatus.ITEM_DELIVERED),
        (TransactionStatus.TRADE_OFFER_SENT_TO_BUYER, TransactionTrigger.Timeout, TransactionStatus.CANCELLED_TIMEOUT),
        (TransactionStatus.TRADE_OFFER_SENT_TO_BUYER, TransactionTrigger.BuyerDecline, TransactionStatus.CANCELLED_BUYER),
        (TransactionStatus.TRADE_OFFER_SENT_TO_BUYER, TransactionTrigger.AdminCancel, TransactionStatus.CANCELLED_ADMIN),

        (TransactionStatus.ITEM_DELIVERED, TransactionTrigger.Complete, TransactionStatus.COMPLETED),

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
    public void EscrowItem_WithoutEscrowBotAssetId_ThrowsInvalidTransition()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER);
        transaction.EscrowBotAssetId = null;
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.EscrowItem));
        Assert.Equal(TransactionStateMachine.InvalidTransitionErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void DeliverItem_WithoutDeliveredBuyerAssetId_ThrowsInvalidTransition()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.TRADE_OFFER_SENT_TO_BUYER);
        transaction.DeliveredBuyerAssetId = null;
        var sm = new TransactionStateMachine(transaction);

        var ex = Assert.Throws<DomainException>(() => sm.Fire(TransactionTrigger.DeliverItem));
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
    public void OnEntry_ItemEscrowedResetsTimeoutWarningSentAt()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER);
        transaction.TimeoutWarningSentAt = DateTime.UtcNow.AddMinutes(-1);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.EscrowItem);

        Assert.Equal(TransactionStatus.ITEM_ESCROWED, transaction.Status);
        Assert.NotNull(transaction.ItemEscrowedAt);
        Assert.Null(transaction.TimeoutWarningSentAt);
    }

    [Fact]
    public void OnExit_FromItemEscrowedClearsTimeoutWarningJobIdAndSentAt()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.ITEM_ESCROWED);
        transaction.TimeoutWarningJobId = "warning-job-1";
        transaction.TimeoutWarningSentAt = DateTime.UtcNow.AddMinutes(-1);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.ConfirmPayment);

        Assert.Null(transaction.TimeoutWarningJobId);
        Assert.Null(transaction.TimeoutWarningSentAt);
    }

    [Fact]
    public void OnEntry_PaymentReceivedSetsPaymentReceivedAt()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.ITEM_ESCROWED);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.ConfirmPayment);

        Assert.NotNull(transaction.PaymentReceivedAt);
    }

    [Fact]
    public void OnEntry_ItemDeliveredSetsItemDeliveredAt()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.TRADE_OFFER_SENT_TO_BUYER);
        var sm = new TransactionStateMachine(transaction);

        sm.Fire(TransactionTrigger.DeliverItem);

        Assert.NotNull(transaction.ItemDeliveredAt);
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
    public void ApplyEmergencyHold_StampsAllFields()
    {
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.ITEM_ESCROWED);
        transaction.PaymentDeadline = DateTime.UtcNow.AddMinutes(30);
        var sm = new TransactionStateMachine(transaction);
        var adminId = Guid.NewGuid();

        sm.ApplyEmergencyHold(adminId, "Sanctions match");

        Assert.True(transaction.IsOnHold);
        Assert.NotNull(transaction.EmergencyHoldAt);
        Assert.Equal("Sanctions match", transaction.EmergencyHoldReason);
        Assert.Equal(adminId, transaction.EmergencyHoldByAdminId);
        Assert.Equal((int)TransactionStatus.ITEM_ESCROWED, transaction.PreviousStatusBeforeHold);
        Assert.Equal(TimeoutFreezeReason.EMERGENCY_HOLD, transaction.TimeoutFreezeReason);
        Assert.NotNull(transaction.TimeoutFrozenAt);
        Assert.NotNull(transaction.TimeoutRemainingSeconds);
        Assert.True(transaction.TimeoutRemainingSeconds > 0);
    }

    [Fact]
    public void ApplyEmergencyHold_OnNonItemEscrowedState_DoesNotSetTimeoutRemainingSeconds()
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
        var transaction = NewTransactionWithAllRequiredFields(TransactionStatus.CREATED);
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
        Assert.DoesNotContain(TransactionTrigger.SendTradeOfferToSeller, permitted);
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
            transaction.EscrowBotAssetId = "ESC-999";
            transaction.DeliveredBuyerAssetId = "DEL-123";
        }

        // Cumulative milestone timestamps per current status (06 §3.5 matrix).
        if (StatusRequiresAccepted(status)) transaction.AcceptedAt = DateTime.UtcNow;
        if (StatusRequiresItemEscrowed(status)) transaction.ItemEscrowedAt = DateTime.UtcNow;
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
        or TransactionStatus.TRADE_OFFER_SENT_TO_SELLER
        or TransactionStatus.ITEM_ESCROWED
        or TransactionStatus.PAYMENT_RECEIVED
        or TransactionStatus.TRADE_OFFER_SENT_TO_BUYER
        or TransactionStatus.ITEM_DELIVERED
        or TransactionStatus.COMPLETED;

    private static bool StatusRequiresItemEscrowed(TransactionStatus s) => s is
        TransactionStatus.ITEM_ESCROWED
        or TransactionStatus.PAYMENT_RECEIVED
        or TransactionStatus.TRADE_OFFER_SENT_TO_BUYER
        or TransactionStatus.ITEM_DELIVERED
        or TransactionStatus.COMPLETED;

    private static bool StatusRequiresItemDelivered(TransactionStatus s) => s is
        TransactionStatus.ITEM_DELIVERED
        or TransactionStatus.COMPLETED;

    private static bool StatusRequiresPaymentReceivedAt(TransactionStatus s) => s is
        TransactionStatus.PAYMENT_RECEIVED
        or TransactionStatus.TRADE_OFFER_SENT_TO_BUYER
        or TransactionStatus.ITEM_DELIVERED
        or TransactionStatus.COMPLETED;

    private static bool StatusIsCancelled(TransactionStatus s) => s is
        TransactionStatus.CANCELLED_TIMEOUT
        or TransactionStatus.CANCELLED_SELLER
        or TransactionStatus.CANCELLED_BUYER
        or TransactionStatus.CANCELLED_ADMIN;

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
        or TransactionTrigger.BuyerDecline;
}
