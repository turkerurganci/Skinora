using Skinora.Platform.Application.Audit;
using Skinora.Shared.Enums;

namespace Skinora.Platform.Tests.Unit.Audit;

/// <summary>
/// Pure-mapping coverage for <see cref="AuditLogCategoryMap"/> — every
/// <see cref="AuditAction"/> value must map to one of the three API
/// categories surfaced by 07 §9.19.
/// </summary>
public class AuditLogCategoryMapTests
{
    [Theory]
    [InlineData(AuditAction.WALLET_DEPOSIT, AuditLogCategoryMap.Categories.FundMovement)]
    [InlineData(AuditAction.WALLET_WITHDRAW, AuditLogCategoryMap.Categories.FundMovement)]
    [InlineData(AuditAction.WALLET_ESCROW_LOCK, AuditLogCategoryMap.Categories.FundMovement)]
    [InlineData(AuditAction.WALLET_ESCROW_RELEASE, AuditLogCategoryMap.Categories.FundMovement)]
    [InlineData(AuditAction.WALLET_REFUND, AuditLogCategoryMap.Categories.FundMovement)]
    [InlineData(AuditAction.DISPUTE_RESOLVED, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.MANUAL_REFUND, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.REFUND_BLOCKED, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.USER_BANNED, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.USER_UNBANNED, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.ROLE_CHANGED, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.SYSTEM_SETTING_CHANGED, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.WALLET_ADDRESS_CHANGED, AuditLogCategoryMap.Categories.SecurityEvent)]
    public void CategoryFor_Maps_06_2_19_Groups_To_API_Categories(
        AuditAction action, string expectedCategory)
    {
        Assert.Equal(expectedCategory, AuditLogCategoryMap.CategoryFor(action));
    }

    [Fact]
    public void Every_AuditAction_Has_A_Category()
    {
        // Guard — when 06 §2.19 grows the enum, this test fails until the map
        // is extended (no silent gaps).
        foreach (var action in Enum.GetValues<AuditAction>())
        {
            var category = AuditLogCategoryMap.CategoryFor(action);
            Assert.False(string.IsNullOrEmpty(category));
        }
    }

    [Fact]
    public void ActionsInCategory_FUND_MOVEMENT_Returns_Five_Wallet_Actions()
    {
        var actions = AuditLogCategoryMap.ActionsInCategory(
            AuditLogCategoryMap.Categories.FundMovement);

        Assert.Equal(5, actions.Count);
        Assert.Contains(AuditAction.WALLET_DEPOSIT, actions);
        Assert.Contains(AuditAction.WALLET_WITHDRAW, actions);
        Assert.Contains(AuditAction.WALLET_ESCROW_LOCK, actions);
        Assert.Contains(AuditAction.WALLET_ESCROW_RELEASE, actions);
        Assert.Contains(AuditAction.WALLET_REFUND, actions);
    }

    [Fact]
    public void ActionsInCategory_ADMIN_ACTION_Returns_Seven_Admin_Actions()
    {
        var actions = AuditLogCategoryMap.ActionsInCategory(
            AuditLogCategoryMap.Categories.AdminAction);

        Assert.Equal(7, actions.Count);
        Assert.Contains(AuditAction.SYSTEM_SETTING_CHANGED, actions);
        Assert.Contains(AuditAction.REFUND_BLOCKED, actions);
    }

    [Fact]
    public void ActionsInCategory_SECURITY_EVENT_Returns_Wallet_Address_Changed()
    {
        var actions = AuditLogCategoryMap.ActionsInCategory(
            AuditLogCategoryMap.Categories.SecurityEvent);

        Assert.Equal(new[] { AuditAction.WALLET_ADDRESS_CHANGED }, actions);
    }

    [Fact]
    public void ActionsInCategory_Unknown_Category_Returns_Empty()
    {
        Assert.Empty(AuditLogCategoryMap.ActionsInCategory("BOGUS_CATEGORY"));
        Assert.Empty(AuditLogCategoryMap.ActionsInCategory(""));
    }

    [Theory]
    [InlineData("FUND_MOVEMENT", true)]
    [InlineData("ADMIN_ACTION", true)]
    [InlineData("SECURITY_EVENT", true)]
    [InlineData("fund_movement", false)] // case sensitive
    [InlineData("RANDOM", false)]
    [InlineData(null, false)]
    public void IsValidCategory_Honors_07_9_19_Enum(string? input, bool expected)
    {
        Assert.Equal(expected, AuditLogCategoryMap.IsValidCategory(input));
    }
}
