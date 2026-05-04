using Skinora.Shared.Enums;

namespace Skinora.Shared.Tests.Unit;

public class EnumTests
{
    // ── TransactionStatus (13) ──────────────────────────────────────

    [Fact]
    public void TransactionStatus_ShouldHave13Values()
    {
        var values = Enum.GetValues<TransactionStatus>();
        Assert.Equal(13, values.Length);
    }

    [Theory]
    [InlineData(nameof(TransactionStatus.CREATED))]
    [InlineData(nameof(TransactionStatus.ACCEPTED))]
    [InlineData(nameof(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER))]
    [InlineData(nameof(TransactionStatus.ITEM_ESCROWED))]
    [InlineData(nameof(TransactionStatus.PAYMENT_RECEIVED))]
    [InlineData(nameof(TransactionStatus.TRADE_OFFER_SENT_TO_BUYER))]
    [InlineData(nameof(TransactionStatus.ITEM_DELIVERED))]
    [InlineData(nameof(TransactionStatus.COMPLETED))]
    [InlineData(nameof(TransactionStatus.CANCELLED_TIMEOUT))]
    [InlineData(nameof(TransactionStatus.CANCELLED_SELLER))]
    [InlineData(nameof(TransactionStatus.CANCELLED_BUYER))]
    [InlineData(nameof(TransactionStatus.CANCELLED_ADMIN))]
    [InlineData(nameof(TransactionStatus.FLAGGED))]
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

    // ── BlockchainTransactionType (9) ───────────────────────────────

    [Fact]
    public void BlockchainTransactionType_ShouldHave9Values()
    {
        var values = Enum.GetValues<BlockchainTransactionType>();
        Assert.Equal(9, values.Length);
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

    // ── TradeOfferDirection (3) ─────────────────────────────────────

    [Fact]
    public void TradeOfferDirection_ShouldHave3Values()
    {
        var values = Enum.GetValues<TradeOfferDirection>();
        Assert.Equal(3, values.Length);
    }

    [Theory]
    [InlineData(nameof(TradeOfferDirection.TO_SELLER))]
    [InlineData(nameof(TradeOfferDirection.TO_BUYER))]
    [InlineData(nameof(TradeOfferDirection.RETURN_TO_SELLER))]
    public void TradeOfferDirection_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(TradeOfferDirection), Enum.Parse<TradeOfferDirection>(valueName)));
    }

    // ── TradeOfferStatus (6) ────────────────────────────────────────

    [Fact]
    public void TradeOfferStatus_ShouldHave6Values()
    {
        var values = Enum.GetValues<TradeOfferStatus>();
        Assert.Equal(6, values.Length);
    }

    [Theory]
    [InlineData(nameof(TradeOfferStatus.PENDING))]
    [InlineData(nameof(TradeOfferStatus.SENT))]
    [InlineData(nameof(TradeOfferStatus.ACCEPTED))]
    [InlineData(nameof(TradeOfferStatus.DECLINED))]
    [InlineData(nameof(TradeOfferStatus.EXPIRED))]
    [InlineData(nameof(TradeOfferStatus.FAILED))]
    public void TradeOfferStatus_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(TradeOfferStatus), Enum.Parse<TradeOfferStatus>(valueName)));
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

    // ── DisputeStatus (3) ───────────────────────────────────────────

    [Fact]
    public void DisputeStatus_ShouldHave3Values()
    {
        var values = Enum.GetValues<DisputeStatus>();
        Assert.Equal(3, values.Length);
    }

    [Theory]
    [InlineData(nameof(DisputeStatus.OPEN))]
    [InlineData(nameof(DisputeStatus.ESCALATED))]
    [InlineData(nameof(DisputeStatus.CLOSED))]
    public void DisputeStatus_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(DisputeStatus), Enum.Parse<DisputeStatus>(valueName)));
    }

    // ── FraudFlagType (4) ───────────────────────────────────────────

    [Fact]
    public void FraudFlagType_ShouldHave4Values()
    {
        var values = Enum.GetValues<FraudFlagType>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(nameof(FraudFlagType.PRICE_DEVIATION))]
    [InlineData(nameof(FraudFlagType.HIGH_VOLUME))]
    [InlineData(nameof(FraudFlagType.ABNORMAL_BEHAVIOR))]
    [InlineData(nameof(FraudFlagType.MULTI_ACCOUNT))]
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

    // ── NotificationType (20) ───────────────────────────────────────

    [Fact]
    public void NotificationType_ShouldHave20Values()
    {
        var values = Enum.GetValues<NotificationType>();
        Assert.Equal(20, values.Length);
    }

    [Theory]
    [InlineData(nameof(NotificationType.TRANSACTION_INVITE))]
    [InlineData(nameof(NotificationType.BUYER_ACCEPTED))]
    [InlineData(nameof(NotificationType.ITEM_ESCROWED))]
    [InlineData(nameof(NotificationType.PAYMENT_RECEIVED))]
    [InlineData(nameof(NotificationType.TRADE_OFFER_SENT_TO_BUYER))]
    [InlineData(nameof(NotificationType.TRANSACTION_COMPLETED))]
    [InlineData(nameof(NotificationType.SELLER_PAYMENT_SENT))]
    [InlineData(nameof(NotificationType.TIMEOUT_WARNING))]
    [InlineData(nameof(NotificationType.TRANSACTION_CANCELLED))]
    [InlineData(nameof(NotificationType.TRANSACTION_FLAGGED))]
    [InlineData(nameof(NotificationType.PAYMENT_INCORRECT))]
    [InlineData(nameof(NotificationType.LATE_PAYMENT_REFUNDED))]
    [InlineData(nameof(NotificationType.ITEM_RETURNED))]
    [InlineData(nameof(NotificationType.PAYMENT_REFUNDED))]
    [InlineData(nameof(NotificationType.DISPUTE_RESULT))]
    [InlineData(nameof(NotificationType.FLAG_RESOLVED))]
    [InlineData(nameof(NotificationType.ADMIN_FLAG_ALERT))]
    [InlineData(nameof(NotificationType.ADMIN_ESCALATION))]
    [InlineData(nameof(NotificationType.ADMIN_PAYMENT_FAILURE))]
    [InlineData(nameof(NotificationType.ADMIN_STEAM_BOT_ISSUE))]
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

    // ── PlatformSteamBotStatus (4) ──────────────────────────────────

    [Fact]
    public void PlatformSteamBotStatus_ShouldHave4Values()
    {
        var values = Enum.GetValues<PlatformSteamBotStatus>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(nameof(PlatformSteamBotStatus.ACTIVE))]
    [InlineData(nameof(PlatformSteamBotStatus.RESTRICTED))]
    [InlineData(nameof(PlatformSteamBotStatus.BANNED))]
    [InlineData(nameof(PlatformSteamBotStatus.OFFLINE))]
    public void PlatformSteamBotStatus_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(PlatformSteamBotStatus), Enum.Parse<PlatformSteamBotStatus>(valueName)));
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

    // ── AuditAction (17) ────────────────────────────────────────────

    [Fact]
    public void AuditAction_ShouldHave17Values()
    {
        var values = Enum.GetValues<AuditAction>();
        Assert.Equal(17, values.Length);
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

    // ── DeliveryStatus (3) ──────────────────────────────────────────

    [Fact]
    public void DeliveryStatus_ShouldHave3Values()
    {
        var values = Enum.GetValues<DeliveryStatus>();
        Assert.Equal(3, values.Length);
    }

    [Theory]
    [InlineData(nameof(DeliveryStatus.PENDING))]
    [InlineData(nameof(DeliveryStatus.SENT))]
    [InlineData(nameof(DeliveryStatus.FAILED))]
    public void DeliveryStatus_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(DeliveryStatus), Enum.Parse<DeliveryStatus>(valueName)));
    }

    // ── TransactionTrigger (15) — 05 §4.2 transition table triggers ───

    [Fact]
    public void TransactionTrigger_ShouldHave15Values()
    {
        var values = Enum.GetValues<TransactionTrigger>();
        Assert.Equal(15, values.Length);
    }

    [Theory]
    [InlineData(nameof(TransactionTrigger.BuyerAccept))]
    [InlineData(nameof(TransactionTrigger.SendTradeOfferToSeller))]
    [InlineData(nameof(TransactionTrigger.EscrowItem))]
    [InlineData(nameof(TransactionTrigger.ConfirmPayment))]
    [InlineData(nameof(TransactionTrigger.SendTradeOfferToBuyer))]
    [InlineData(nameof(TransactionTrigger.DeliverItem))]
    [InlineData(nameof(TransactionTrigger.Complete))]
    [InlineData(nameof(TransactionTrigger.Timeout))]
    [InlineData(nameof(TransactionTrigger.SellerCancel))]
    [InlineData(nameof(TransactionTrigger.BuyerCancel))]
    [InlineData(nameof(TransactionTrigger.AdminCancel))]
    [InlineData(nameof(TransactionTrigger.SellerDecline))]
    [InlineData(nameof(TransactionTrigger.BuyerDecline))]
    [InlineData(nameof(TransactionTrigger.AdminApprove))]
    [InlineData(nameof(TransactionTrigger.AdminReject))]
    public void TransactionTrigger_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(TransactionTrigger), Enum.Parse<TransactionTrigger>(valueName)));
    }

    [Fact]
    public void ItemRefundTrigger_ShouldHave4Values()
    {
        var values = Enum.GetValues<ItemRefundTrigger>();
        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(nameof(ItemRefundTrigger.TimeoutPayment))]
    [InlineData(nameof(ItemRefundTrigger.TimeoutDelivery))]
    [InlineData(nameof(ItemRefundTrigger.SellerCancel))]
    [InlineData(nameof(ItemRefundTrigger.BuyerCancel))]
    public void ItemRefundTrigger_ShouldContainExpectedValue(string valueName)
    {
        Assert.True(Enum.IsDefined(typeof(ItemRefundTrigger), Enum.Parse<ItemRefundTrigger>(valueName)));
    }

    // ── Cross-cutting ───────────────────────────────────────────────

    [Fact]
    public void AllEnums_ShouldExistInSharedNamespace()
    {
        var enumTypes = typeof(TransactionStatus).Assembly
            .GetTypes()
            .Where(t => t.IsEnum && t.Namespace == "Skinora.Shared.Enums")
            .ToList();

        Assert.Equal(26, enumTypes.Count);
    }
}
