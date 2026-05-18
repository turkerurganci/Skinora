using Skinora.Notifications.Infrastructure.Email;
using Skinora.Shared.Email;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Tests.Unit.Email;

public sealed class EmailCategoryMapTests
{
    [Theory]
    [InlineData(NotificationType.PAYMENT_RECEIVED, EmailCategory.Transaction)]
    [InlineData(NotificationType.TRANSACTION_COMPLETED, EmailCategory.Transaction)]
    [InlineData(NotificationType.TIMEOUT_WARNING, EmailCategory.Timeout)]
    [InlineData(NotificationType.EMERGENCY_HOLD_APPLIED, EmailCategory.Security)]
    [InlineData(NotificationType.TRANSACTION_FLAGGED, EmailCategory.Security)]
    [InlineData(NotificationType.ADMIN_FLAG_ALERT, EmailCategory.Account)]
    public void Resolve_KnownType_ReturnsCategory(NotificationType type, EmailCategory expected)
    {
        Assert.Equal(expected, EmailCategoryMap.Resolve(type));
    }

    [Fact]
    public void Resolve_EveryNotificationTypeHasMapping()
    {
        // Exhaustiveness: a missing entry would silently mis-categorise a
        // production email. EmailCategoryMap throws when unknown; turn that
        // into a per-value sweep so the failure points at the offending
        // enum value.
        foreach (var type in Enum.GetValues<NotificationType>())
        {
            var category = EmailCategoryMap.Resolve(type);
            Assert.True(Enum.IsDefined(category));
        }
    }

    [Fact]
    public void ResolveTag_IsLowercaseCategoryName()
    {
        Assert.Equal("transaction", EmailCategoryMap.ResolveTag(NotificationType.PAYMENT_RECEIVED));
        Assert.Equal("security", EmailCategoryMap.ResolveTag(NotificationType.EMERGENCY_HOLD_APPLIED));
        Assert.Equal("timeout", EmailCategoryMap.ResolveTag(NotificationType.TIMEOUT_WARNING));
    }
}
