using Skinora.Shared.Enums;

namespace Skinora.Shared.Tests.Unit;

public class EnumTests
{
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

    [Fact]
    public void StablecoinType_ShouldHave2Values()
    {
        var values = Enum.GetValues<StablecoinType>();
        Assert.Equal(2, values.Length);
        Assert.Contains(StablecoinType.USDT, values);
        Assert.Contains(StablecoinType.USDC, values);
    }

    [Fact]
    public void BuyerIdentificationMethod_ShouldHave2Values()
    {
        var values = Enum.GetValues<BuyerIdentificationMethod>();
        Assert.Equal(2, values.Length);
    }

    [Fact]
    public void CancelledByType_ShouldHave4Values()
    {
        var values = Enum.GetValues<CancelledByType>();
        Assert.Equal(4, values.Length);
    }

    [Fact]
    public void BlockchainTransactionType_ShouldHave9Values()
    {
        var values = Enum.GetValues<BlockchainTransactionType>();
        Assert.Equal(9, values.Length);
    }

    [Fact]
    public void BlockchainTransactionStatus_ShouldHave4Values()
    {
        var values = Enum.GetValues<BlockchainTransactionStatus>();
        Assert.Equal(4, values.Length);
    }

    [Fact]
    public void TradeOfferDirection_ShouldHave3Values()
    {
        var values = Enum.GetValues<TradeOfferDirection>();
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public void TradeOfferStatus_ShouldHave6Values()
    {
        var values = Enum.GetValues<TradeOfferStatus>();
        Assert.Equal(6, values.Length);
    }

    [Fact]
    public void DisputeType_ShouldHave3Values()
    {
        var values = Enum.GetValues<DisputeType>();
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public void DisputeStatus_ShouldHave3Values()
    {
        var values = Enum.GetValues<DisputeStatus>();
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public void FraudFlagType_ShouldHave4Values()
    {
        var values = Enum.GetValues<FraudFlagType>();
        Assert.Equal(4, values.Length);
    }

    [Fact]
    public void ReviewStatus_ShouldHave3Values()
    {
        var values = Enum.GetValues<ReviewStatus>();
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public void NotificationType_ShouldHave20Values()
    {
        var values = Enum.GetValues<NotificationType>();
        Assert.Equal(20, values.Length);
    }

    [Fact]
    public void NotificationChannel_ShouldHave3Values()
    {
        var values = Enum.GetValues<NotificationChannel>();
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public void PlatformSteamBotStatus_ShouldHave4Values()
    {
        var values = Enum.GetValues<PlatformSteamBotStatus>();
        Assert.Equal(4, values.Length);
    }

    [Fact]
    public void MonitoringStatus_ShouldHave5Values()
    {
        var values = Enum.GetValues<MonitoringStatus>();
        Assert.Equal(5, values.Length);
    }

    [Fact]
    public void OutboxMessageStatus_ShouldHave4Values()
    {
        var values = Enum.GetValues<OutboxMessageStatus>();
        Assert.Equal(4, values.Length);
    }

    [Fact]
    public void ActorType_ShouldHave3Values()
    {
        var values = Enum.GetValues<ActorType>();
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public void AuditAction_ShouldHave12Values()
    {
        var values = Enum.GetValues<AuditAction>();
        Assert.Equal(12, values.Length);
    }

    [Fact]
    public void TimeoutFreezeReason_ShouldHave4Values()
    {
        var values = Enum.GetValues<TimeoutFreezeReason>();
        Assert.Equal(4, values.Length);
    }

    [Fact]
    public void FraudFlagScope_ShouldHave2Values()
    {
        var values = Enum.GetValues<FraudFlagScope>();
        Assert.Equal(2, values.Length);
    }

    [Fact]
    public void PayoutIssueStatus_ShouldHave5Values()
    {
        var values = Enum.GetValues<PayoutIssueStatus>();
        Assert.Equal(5, values.Length);
    }

    [Fact]
    public void DeliveryStatus_ShouldHave3Values()
    {
        var values = Enum.GetValues<DeliveryStatus>();
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public void AllEnums_ShouldExistInSharedNamespace()
    {
        var enumTypes = typeof(TransactionStatus).Assembly
            .GetTypes()
            .Where(t => t.IsEnum && t.Namespace == "Skinora.Shared.Enums")
            .ToList();

        Assert.Equal(23, enumTypes.Count);
    }
}
