using Skinora.Notifications.Application.Inbox;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Tests.Unit;

/// <summary>
/// Coverage for <see cref="NotificationTargetMapper"/> — the helper that
/// derives the (targetType, targetId) pair returned by 07 §8.1.
/// </summary>
public class NotificationTargetMapperTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(NotificationType.BUYER_ACCEPTED)]
    [InlineData(NotificationType.PAYMENT_RECEIVED)]
    [InlineData(NotificationType.TRANSACTION_COMPLETED)]
    [InlineData(NotificationType.TIMEOUT_WARNING)]
    [InlineData(NotificationType.TRANSACTION_FLAGGED)]
    [InlineData(NotificationType.DISPUTE_RESULT)]
    [InlineData(NotificationType.ADMIN_ESCALATION)]
    [InlineData(NotificationType.ADMIN_PAYMENT_FAILURE)]
    public void Resolve_TransactionTypes_WithTransactionId_ReturnsTransactionTarget(
        NotificationType type)
    {
        var transactionId = Guid.NewGuid();

        var (targetType, targetId) = NotificationTargetMapper.Resolve(type, transactionId);

        Assert.Equal("transaction", targetType);
        Assert.Equal(transactionId, targetId);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(NotificationType.BUYER_ACCEPTED)]
    [InlineData(NotificationType.PAYMENT_RECEIVED)]
    public void Resolve_TransactionTypes_WithoutTransactionId_ReturnsNullPair(
        NotificationType type)
    {
        var (targetType, targetId) = NotificationTargetMapper.Resolve(type, transactionId: null);

        Assert.Null(targetType);
        Assert.Null(targetId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resolve_AdminFlagAlert_ReturnsFlagTargetType()
    {
        var id = Guid.NewGuid();

        var (targetType, targetId) = NotificationTargetMapper.Resolve(
            NotificationType.ADMIN_FLAG_ALERT, id);

        Assert.Equal("flag", targetType);
        Assert.Equal(id, targetId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resolve_AdminSteamBotIssue_AlwaysReturnsNullPair()
    {
        var (targetType, targetId) = NotificationTargetMapper.Resolve(
            NotificationType.ADMIN_STEAM_BOT_ISSUE, Guid.NewGuid());

        Assert.Null(targetType);
        Assert.Null(targetId);
    }
}
