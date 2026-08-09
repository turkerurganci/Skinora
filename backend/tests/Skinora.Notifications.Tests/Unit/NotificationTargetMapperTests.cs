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
    public void Resolve_AdminFlagAlert_WithFlagId_ReturnsFlagTarget()
    {
        var flagId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        var (targetType, targetId) = NotificationTargetMapper.Resolve(
            NotificationType.ADMIN_FLAG_ALERT, transactionId, flagId);

        // WP8 — flag alert resolves to its dedicated FlagId column, not the
        // TransactionId the earlier implementation reinterpreted as a flag id.
        Assert.Equal("flag", targetType);
        Assert.Equal(flagId, targetId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resolve_AdminFlagAlert_WithoutFlagId_ReturnsNullPair()
    {
        var (targetType, targetId) = NotificationTargetMapper.Resolve(
            NotificationType.ADMIN_FLAG_ALERT, transactionId: Guid.NewGuid(), flagId: null);

        Assert.Null(targetType);
        Assert.Null(targetId);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resolve_AdminPlatformOutage_AlwaysReturnsNullPair()
    {
        // Platform-wide admin alert: it has no per-entity target even when a
        // transaction id happens to be attached. (v3.0 — this case used to be
        // covered by ADMIN_STEAM_BOT_ISSUE, retired with the bot layer.)
        var (targetType, targetId) = NotificationTargetMapper.Resolve(
            NotificationType.ADMIN_PLATFORM_OUTAGE, Guid.NewGuid());

        Assert.Null(targetType);
        Assert.Null(targetId);
    }
}
