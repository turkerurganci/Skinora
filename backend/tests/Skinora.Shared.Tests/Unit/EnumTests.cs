using Skinora.Shared.Enums;

namespace Skinora.Shared.Tests.Unit;

public class EnumTests
{
    // ── TransactionStatus (12) ──────────────────────────────────────

    [Fact]
    public void TransactionStatus_ShouldHave12Values()
    {
        // 12 after the v3.0 P2P pivot: TRADE_OFFER_SENT_TO_SELLER became
        // SELLER_CONFIRMED, and ITEM_ESCROWED / TRADE_OFFER_SENT_TO_BUYER were
        // removed — the platform neither escrows items nor sends trade offers
        // (02 §2.1).
        var values = Enum.GetValues<TransactionStatus>();
        Assert.Equal(12, values.Length);
    }

    [Theory]
    [InlineData(nameof(TransactionStatus.CREATED))]
    [InlineData(nameof(TransactionStatus.ACCEPTED))]
    [InlineData(nameof(TransactionStatus.SELLER_CONFIRMED))]
    [InlineData(nameof(TransactionStatus.PAYMENT_RECEIVED))]
    [InlineData(nameof(TransactionStatus.ITEM_DELIVERED))]
    [InlineData(nameof(TransactionStatus.COMPLETED))]
    [InlineData(nameof(TransactionStatus.CANCELLED_TIMEOUT))]
    [InlineData(nameof(TransactionStatus.CANCELLED_SELLER))]
    [InlineData(nameof(TransactionStatus.CANCELLED_BUYER))]
    [InlineData(nameof(TransactionStatus.CANCELLED_ADMIN))]
    [InlineData(nameof(TransactionStatus.FLAGGED))]
    [InlineData(nameof(TransactionStatus.REFUNDED))]
    public void TransactionStatus_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(TransactionStatus), Enum.Parse<TransactionStatus>(valueName)));
    }

    // ── StablecoinType (2) ──────────────────────────────────────────

    [Fact]
    public void StablecoinType_ShouldHave2Values()
    {
        var values = Enum.GetValues<StablecoinType>();
        Assert.Equal(2, values.Length);
    }

    [Theory]
    [InlineData(nameof(StablecoinType.USDT))]
    [InlineData(nameof(StablecoinType.USDC))]
    public void StablecoinType_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(StablecoinType), Enum.Parse<StablecoinType>(valueName)));
    }

    // ── BuyerIdentificationMethod (2) ───────────────────────────────

    [Fact]
    public void BuyerIdentificationMethod_ShouldHave2Values()
    {
        var values = Enum.GetValues<BuyerIdentificationMethod>();
        Assert.Equal(2, values.Length);
    }

    [Theory]
    [InlineData(nameof(BuyerIdentificationMethod.STEAM_ID))]
    [InlineData(nameof(BuyerIdentificationMethod.OPEN_LINK))]
    public void BuyerIdentificationMethod_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(BuyerIdentificationMethod), Enum.Parse<BuyerIdentificationMethod>(valueName)));
    }

    // ── CancelledByType (4) ─────────────────────────────────────────

    [Fact]
    public void CancelledByType_ShouldHave4Values()
    {
        var values = Enum.GetValues<CancelledByType>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(nameof(CancelledByType.TIMEOUT))]
    [InlineData(nameof(CancelledByType.SELLER))]
    [InlineData(nameof(CancelledByType.BUYER))]
    [InlineData(nameof(CancelledByType.ADMIN))]
    public void CancelledByType_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(CancelledByType), Enum.Parse<CancelledByType>(valueName)));
    }

    // ── BlockchainTransactionType (10) ──────────────────────────────

    [Fact]
    public void BlockchainTransactionType_ShouldHave10Values()
    {
        var values = Enum.GetValues<BlockchainTransactionType>();
        Assert.Equal(10, values.Length);
    }

    [Theory]
    [InlineData(nameof(BlockchainTransactionType.BUYER_PAYMENT))]
    [InlineData(nameof(BlockchainTransactionType.SELLER_PAYOUT))]
    [InlineData(nameof(BlockchainTransactionType.BUYER_REFUND))]
    [InlineData(nameof(BlockchainTransactionType.EXCESS_REFUND))]
    [InlineData(nameof(BlockchainTransactionType.WRONG_TOKEN_INCOMING))]
    [InlineData(nameof(BlockchainTransactionType.WRONG_TOKEN_REFUND))]
    [InlineData(nameof(BlockchainTransactionType.SPAM_TOKEN_INCOMING))]
    [InlineData(nameof(BlockchainTransactionType.LATE_PAYMENT_REFUND))]
    [InlineData(nameof(BlockchainTransactionType.INCORRECT_AMOUNT_REFUND))]
    [InlineData(nameof(BlockchainTransactionType.SWEEP))]
    public void BlockchainTransactionType_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(BlockchainTransactionType), Enum.Parse<BlockchainTransactionType>(valueName)));
    }

    // ── BlockchainTransactionStatus (4) ─────────────────────────────

    [Fact]
    public void BlockchainTransactionStatus_ShouldHave4Values()
    {
        var values = Enum.GetValues<BlockchainTransactionStatus>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(nameof(BlockchainTransactionStatus.DETECTED))]
    [InlineData(nameof(BlockchainTransactionStatus.PENDING))]
    [InlineData(nameof(BlockchainTransactionStatus.CONFIRMED))]
    [InlineData(nameof(BlockchainTransactionStatus.FAILED))]
    public void BlockchainTransactionStatus_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(BlockchainTransactionStatus), Enum.Parse<BlockchainTransactionStatus>(valueName)));
    }

    // ── DisputeType (3) ─────────────────────────────────────────────

    [Fact]
    public void DisputeType_ShouldHave3Values()
    {
        var values = Enum.GetValues<DisputeType>();
        Assert.Equal(3, values.Length);
    }

    [Theory]
    [InlineData(nameof(DisputeType.PAYMENT))]
    [InlineData(nameof(DisputeType.DELIVERY))]
    [InlineData(nameof(DisputeType.WRONG_ITEM))]
    public void DisputeType_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(DisputeType), Enum.Parse<DisputeType>(valueName)));
    }

    // ── DisputeStatus (5) ───────────────────────────────────────────

    [Fact]
    public void DisputeStatus_ShouldHave5Values()
    {
        // 5 after WP5 added RESOLVED_FOR_SELLER / RESOLVED_FOR_BUYER
        // (admin dispute resolution terminals).
        var values = Enum.GetValues<DisputeStatus>();
        Assert.Equal(5, values.Length);
    }

    [Theory]
    [InlineData(nameof(DisputeStatus.OPEN))]
    [InlineData(nameof(DisputeStatus.ESCALATED))]
    [InlineData(nameof(DisputeStatus.CLOSED))]
    [InlineData(nameof(DisputeStatus.RESOLVED_FOR_SELLER))]
    [InlineData(nameof(DisputeStatus.RESOLVED_FOR_BUYER))]
    public void DisputeStatus_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(DisputeStatus), Enum.Parse<DisputeStatus>(valueName)));
    }

    // ── DisputeResolutionOutcome (2) ────────────────────────────────

    [Fact]
    public void DisputeResolutionOutcome_ShouldHave2Values()
    {
        var values = Enum.GetValues<DisputeResolutionOutcome>();
        Assert.Equal(2, values.Length);
    }

    [Theory]
    [InlineData(nameof(DisputeResolutionOutcome.SELLER_FAVOR))]
    [InlineData(nameof(DisputeResolutionOutcome.BUYER_FAVOR))]
    public void DisputeResolutionOutcome_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(DisputeResolutionOutcome), Enum.Parse<DisputeResolutionOutcome>(valueName)));
    }

    // ── FraudFlagType (4) ───────────────────────────────────────────

    [Fact]
    public void FraudFlagType_ShouldHave5Values()
    {
        // 5 after T82 added SANCTIONS_MATCH (02 §21.1, 06 §2.11).
        var values = Enum.GetValues<FraudFlagType>();
        Assert.Equal(5, values.Length);
    }

    [Theory]
    [InlineData(nameof(FraudFlagType.PRICE_DEVIATION))]
    [InlineData(nameof(FraudFlagType.HIGH_VOLUME))]
    [InlineData(nameof(FraudFlagType.ABNORMAL_BEHAVIOR))]
    [InlineData(nameof(FraudFlagType.MULTI_ACCOUNT))]
    [InlineData(nameof(FraudFlagType.SANCTIONS_MATCH))]
    public void FraudFlagType_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(FraudFlagType), Enum.Parse<FraudFlagType>(valueName)));
    }

    // ── ReviewStatus (3) ────────────────────────────────────────────

    [Fact]
    public void ReviewStatus_ShouldHave3Values()
    {
        var values = Enum.GetValues<ReviewStatus>();
        Assert.Equal(3, values.Length);
    }

    [Theory]
    [InlineData(nameof(ReviewStatus.PENDING))]
    [InlineData(nameof(ReviewStatus.APPROVED))]
    [InlineData(nameof(ReviewStatus.REJECTED))]
    public void ReviewStatus_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(ReviewStatus), Enum.Parse<ReviewStatus>(valueName)));
    }

    // ── NotificationType (26) ───────────────────────────────────────

    [Fact]
    public void NotificationType_ShouldHave26Values()
    {
        // 26 after the v3.0 P2P pivot: ITEM_ESCROWED became PAYMENT_WINDOW_OPEN
        // and TRADE_OFFER_SENT_TO_BUYER became DELIVERY_EXPECTED (which also
        // flipped recipient — it now addresses the seller); ITEM_RETURNED and
        // ADMIN_STEAM_BOT_ISSUE were removed outright.
        var values = Enum.GetValues<NotificationType>();
        Assert.Equal(26, values.Length);
    }

    [Theory]
    [InlineData(nameof(NotificationType.TRANSACTION_INVITE))]
    [InlineData(nameof(NotificationType.BUYER_ACCEPTED))]
    [InlineData(nameof(NotificationType.PAYMENT_WINDOW_OPEN))]
    [InlineData(nameof(NotificationType.PAYMENT_RECEIVED))]
    [InlineData(nameof(NotificationType.DELIVERY_EXPECTED))]
    [InlineData(nameof(NotificationType.TRANSACTION_COMPLETED))]
    [InlineData(nameof(NotificationType.SELLER_PAYMENT_SENT))]
    [InlineData(nameof(NotificationType.TIMEOUT_WARNING))]
    [InlineData(nameof(NotificationType.TRANSACTION_CANCELLED))]
    [InlineData(nameof(NotificationType.TRANSACTION_FLAGGED))]
    [InlineData(nameof(NotificationType.PAYMENT_INCORRECT))]
    [InlineData(nameof(NotificationType.LATE_PAYMENT_REFUNDED))]
    [InlineData(nameof(NotificationType.PAYMENT_REFUNDED))]
    [InlineData(nameof(NotificationType.DISPUTE_RESULT))]
    [InlineData(nameof(NotificationType.FLAG_RESOLVED))]
    [InlineData(nameof(NotificationType.ADMIN_FLAG_ALERT))]
    [InlineData(nameof(NotificationType.ADMIN_ESCALATION))]
    [InlineData(nameof(NotificationType.ADMIN_PAYMENT_FAILURE))]
    [InlineData(nameof(NotificationType.EMERGENCY_HOLD_APPLIED))]
    [InlineData(nameof(NotificationType.EMERGENCY_HOLD_RELEASED))]
    [InlineData(nameof(NotificationType.INSUFFICIENT_PAYMENT))]
    [InlineData(nameof(NotificationType.OVERPAYMENT_REFUNDED))]
    [InlineData(nameof(NotificationType.WRONG_TOKEN_REFUND))]
    [InlineData(nameof(NotificationType.ACCOUNT_SUSPENDED))]
    [InlineData(nameof(NotificationType.ACCOUNT_UNSUSPENDED))]
    [InlineData(nameof(NotificationType.ADMIN_PLATFORM_OUTAGE))]
    public void NotificationType_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(NotificationType), Enum.Parse<NotificationType>(valueName)));
    }

    // ── NotificationChannel (3) ─────────────────────────────────────

    [Fact]
    public void NotificationChannel_ShouldHave3Values()
    {
        var values = Enum.GetValues<NotificationChannel>();
        Assert.Equal(3, values.Length);
    }

    [Theory]
    [InlineData(nameof(NotificationChannel.EMAIL))]
    [InlineData(nameof(NotificationChannel.TELEGRAM))]
    [InlineData(nameof(NotificationChannel.DISCORD))]
    public void NotificationChannel_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(NotificationChannel), Enum.Parse<NotificationChannel>(valueName)));
    }

    // ── MonitoringStatus (5) ────────────────────────────────────────

    [Fact]
    public void MonitoringStatus_ShouldHave5Values()
    {
        var values = Enum.GetValues<MonitoringStatus>();
        Assert.Equal(5, values.Length);
    }

    [Theory]
    [InlineData(nameof(MonitoringStatus.ACTIVE))]
    [InlineData(nameof(MonitoringStatus.POST_CANCEL_24H))]
    [InlineData(nameof(MonitoringStatus.POST_CANCEL_7D))]
    [InlineData(nameof(MonitoringStatus.POST_CANCEL_30D))]
    [InlineData(nameof(MonitoringStatus.STOPPED))]
    public void MonitoringStatus_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(MonitoringStatus), Enum.Parse<MonitoringStatus>(valueName)));
    }

    // ── OutboxMessageStatus (4) ─────────────────────────────────────

    [Fact]
    public void OutboxMessageStatus_ShouldHave4Values()
    {
        var values = Enum.GetValues<OutboxMessageStatus>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(nameof(OutboxMessageStatus.PENDING))]
    [InlineData(nameof(OutboxMessageStatus.PROCESSED))]
    [InlineData(nameof(OutboxMessageStatus.DEFERRED))]
    [InlineData(nameof(OutboxMessageStatus.FAILED))]
    public void OutboxMessageStatus_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(OutboxMessageStatus), Enum.Parse<OutboxMessageStatus>(valueName)));
    }

    // ── ActorType (3) ───────────────────────────────────────────────

    [Fact]
    public void ActorType_ShouldHave3Values()
    {
        var values = Enum.GetValues<ActorType>();
        Assert.Equal(3, values.Length);
    }

    [Theory]
    [InlineData(nameof(ActorType.USER))]
    [InlineData(nameof(ActorType.SYSTEM))]
    [InlineData(nameof(ActorType.ADMIN))]
    public void ActorType_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(ActorType), Enum.Parse<ActorType>(valueName)));
    }

    // ── AuditAction (32) ────────────────────────────────────────────

    [Fact]
    public void AuditAction_ShouldHave32Values()
    {
        // 28 after T103b-2 added BOT_RECOVERY_ITEM_CREATED / BOT_RECOVERY_UPDATED;
        // 29 after WP7 added MAINTENANCE_MODE_CHANGED;
        // 30 after WP8 added BOT_SESSION_FAILED;
        // 32 after WP16 added TIMEOUT_AUTO_EXTENDED / PLATFORM_OUTAGE_DETECTED.
        var values = Enum.GetValues<AuditAction>();
        Assert.Equal(32, values.Length);
    }

    [Theory]
    [InlineData(nameof(AuditAction.WALLET_DEPOSIT))]
    [InlineData(nameof(AuditAction.WALLET_WITHDRAW))]
    [InlineData(nameof(AuditAction.WALLET_ESCROW_LOCK))]
    [InlineData(nameof(AuditAction.WALLET_ESCROW_RELEASE))]
    [InlineData(nameof(AuditAction.WALLET_REFUND))]
    [InlineData(nameof(AuditAction.DISPUTE_RESOLVED))]
    [InlineData(nameof(AuditAction.MANUAL_REFUND))]
    [InlineData(nameof(AuditAction.REFUND_BLOCKED))]
    [InlineData(nameof(AuditAction.USER_BANNED))]
    [InlineData(nameof(AuditAction.USER_UNBANNED))]
    [InlineData(nameof(AuditAction.ROLE_CHANGED))]
    [InlineData(nameof(AuditAction.SYSTEM_SETTING_CHANGED))]
    [InlineData(nameof(AuditAction.WALLET_ADDRESS_CHANGED))]
    [InlineData(nameof(AuditAction.FRAUD_FLAG_CREATED))]
    [InlineData(nameof(AuditAction.FRAUD_FLAG_APPROVED))]
    [InlineData(nameof(AuditAction.FRAUD_FLAG_REJECTED))]
    [InlineData(nameof(AuditAction.FRAUD_FLAG_AUTO_HOLD))]
    [InlineData(nameof(AuditAction.TRANSACTION_CANCELLED_ADMIN))]
    [InlineData(nameof(AuditAction.EMERGENCY_HOLD_APPLIED))]
    [InlineData(nameof(AuditAction.EMERGENCY_HOLD_RELEASED))]
    [InlineData(nameof(AuditAction.BOT_STATUS_CHANGED))]
    [InlineData(nameof(AuditAction.BOT_SESSION_FAILED))]
    [InlineData(nameof(AuditAction.RECONCILIATION_MISMATCH))]
    [InlineData(nameof(AuditAction.COLD_WALLET_TRANSFER_INITIATED))]
    [InlineData(nameof(AuditAction.HOT_WALLET_THRESHOLD_BREACHED))]
    [InlineData(nameof(AuditAction.SANCTIONS_LIST_ADDRESS_ADDED))]
    [InlineData(nameof(AuditAction.SANCTIONS_LIST_ADDRESS_REMOVED))]
    [InlineData(nameof(AuditAction.BOT_RECOVERY_ITEM_CREATED))]
    [InlineData(nameof(AuditAction.BOT_RECOVERY_UPDATED))]
    [InlineData(nameof(AuditAction.MAINTENANCE_MODE_CHANGED))]
    [InlineData(nameof(AuditAction.TIMEOUT_AUTO_EXTENDED))]
    [InlineData(nameof(AuditAction.PLATFORM_OUTAGE_DETECTED))]
    public void AuditAction_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(AuditAction), Enum.Parse<AuditAction>(valueName)));
    }

    // ── TimeoutFreezeReason (4) ─────────────────────────────────────

    [Fact]
    public void TimeoutFreezeReason_ShouldHave4Values()
    {
        var values = Enum.GetValues<TimeoutFreezeReason>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(nameof(TimeoutFreezeReason.MAINTENANCE))]
    [InlineData(nameof(TimeoutFreezeReason.STEAM_OUTAGE))]
    [InlineData(nameof(TimeoutFreezeReason.BLOCKCHAIN_DEGRADATION))]
    [InlineData(nameof(TimeoutFreezeReason.EMERGENCY_HOLD))]
    public void TimeoutFreezeReason_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(TimeoutFreezeReason), Enum.Parse<TimeoutFreezeReason>(valueName)));
    }

    // ── FraudFlagScope (2) ──────────────────────────────────────────

    [Fact]
    public void FraudFlagScope_ShouldHave2Values()
    {
        var values = Enum.GetValues<FraudFlagScope>();
        Assert.Equal(2, values.Length);
    }

    [Theory]
    [InlineData(nameof(FraudFlagScope.ACCOUNT_LEVEL))]
    [InlineData(nameof(FraudFlagScope.TRANSACTION_PRE_CREATE))]
    public void FraudFlagScope_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(FraudFlagScope), Enum.Parse<FraudFlagScope>(valueName)));
    }

    // ── PayoutIssueStatus (5) ───────────────────────────────────────

    [Fact]
    public void PayoutIssueStatus_ShouldHave5Values()
    {
        var values = Enum.GetValues<PayoutIssueStatus>();
        Assert.Equal(5, values.Length);
    }

    [Theory]
    [InlineData(nameof(PayoutIssueStatus.REPORTED))]
    [InlineData(nameof(PayoutIssueStatus.VERIFYING))]
    [InlineData(nameof(PayoutIssueStatus.RETRY_SCHEDULED))]
    [InlineData(nameof(PayoutIssueStatus.ESCALATED))]
    [InlineData(nameof(PayoutIssueStatus.RESOLVED))]
    public void PayoutIssueStatus_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(PayoutIssueStatus), Enum.Parse<PayoutIssueStatus>(valueName)));
    }

    // ── DeliveryStatus (4) — T78 added DEFERRED ─────────────────────

    [Fact]
    public void DeliveryStatus_ShouldHave4Values()
    {
        var values = Enum.GetValues<DeliveryStatus>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(nameof(DeliveryStatus.PENDING))]
    [InlineData(nameof(DeliveryStatus.SENT))]
    [InlineData(nameof(DeliveryStatus.DEFERRED))]
    [InlineData(nameof(DeliveryStatus.FAILED))]
    public void DeliveryStatus_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(DeliveryStatus), Enum.Parse<DeliveryStatus>(valueName)));
    }

    // ── TransactionTrigger (14) — 05 §4.2 transition table triggers ───

    [Fact]
    public void TransactionTrigger_ShouldHave14Values()
    {
        // 14 after the v3.0 P2P pivot: SellerConfirmReady and DeliveryReversed
        // added; SendTradeOfferToSeller / EscrowItem / SendTradeOfferToBuyer /
        // BuyerDecline removed with the bot custody layer.
        var values = Enum.GetValues<TransactionTrigger>();
        Assert.Equal(14, values.Length);
    }

    [Theory]
    [InlineData(nameof(TransactionTrigger.BuyerAccept))]
    [InlineData(nameof(TransactionTrigger.SellerConfirmReady))]
    [InlineData(nameof(TransactionTrigger.ConfirmPayment))]
    [InlineData(nameof(TransactionTrigger.DeliverItem))]
    [InlineData(nameof(TransactionTrigger.Complete))]
    [InlineData(nameof(TransactionTrigger.DeliveryReversed))]
    [InlineData(nameof(TransactionTrigger.Timeout))]
    [InlineData(nameof(TransactionTrigger.SellerCancel))]
    [InlineData(nameof(TransactionTrigger.BuyerCancel))]
    [InlineData(nameof(TransactionTrigger.AdminCancel))]
    [InlineData(nameof(TransactionTrigger.SellerDecline))]
    [InlineData(nameof(TransactionTrigger.AdminApprove))]
    [InlineData(nameof(TransactionTrigger.AdminReject))]
    public void TransactionTrigger_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(TransactionTrigger), Enum.Parse<TransactionTrigger>(valueName)));
    }

    [Fact]
    public void EmergencyHoldReleaseAction_ShouldHave2Values()
    {
        var values = Enum.GetValues<EmergencyHoldReleaseAction>();
        Assert.Equal(2, values.Length);
    }

    [Theory]
    [InlineData(nameof(EmergencyHoldReleaseAction.RESUME))]
    [InlineData(nameof(EmergencyHoldReleaseAction.CANCEL))]
    public void EmergencyHoldReleaseAction_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(EmergencyHoldReleaseAction), Enum.Parse<EmergencyHoldReleaseAction>(valueName)));
    }

    // ── Cross-cutting ───────────────────────────────────────────────

    [Fact]
    public void AllEnums_ShouldExistInSharedNamespace()
    {
        var enumTypes = typeof(TransactionStatus).Assembly
            .GetTypes()
            .Where(t => t.IsEnum && t.Namespace == "Skinora.Shared.Enums")
            .ToList();

        // 25 after the v3.0 P2P pivot removed 5 bot/trade enums (TradeOfferDirection,
        // TradeOfferStatus, PlatformSteamBotStatus, BotRecoveryStatus, ItemRefundTrigger)
        // and added DeliveryEvidence.
        Assert.Equal(25, enumTypes.Count);
    }
}
