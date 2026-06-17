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
    [InlineData(AuditAction.FRAUD_FLAG_CREATED, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.FRAUD_FLAG_APPROVED, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.FRAUD_FLAG_REJECTED, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.FRAUD_FLAG_AUTO_HOLD, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.TRANSACTION_CANCELLED_ADMIN, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.EMERGENCY_HOLD_APPLIED, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.EMERGENCY_HOLD_RELEASED, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.COLD_WALLET_TRANSFER_INITIATED, AuditLogCategoryMap.Categories.FundMovement)]
    [InlineData(AuditAction.HOT_WALLET_THRESHOLD_BREACHED, AuditLogCategoryMap.Categories.SecurityEvent)]
    [InlineData(AuditAction.BOT_RECOVERY_ITEM_CREATED, AuditLogCategoryMap.Categories.SecurityEvent)]
    [InlineData(AuditAction.BOT_RECOVERY_UPDATED, AuditLogCategoryMap.Categories.AdminAction)]
    [InlineData(AuditAction.MAINTENANCE_MODE_CHANGED, AuditLogCategoryMap.Categories.AdminAction)]
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
    public void ActionsInCategory_FUND_MOVEMENT_Returns_Wallet_Actions_And_ColdWalletTransfer()
    {
        var actions = AuditLogCategoryMap.ActionsInCategory(
            AuditLogCategoryMap.Categories.FundMovement);

        // 5 wallet base + 1 T77 hot→cold consolidation = 6.
        Assert.Equal(6, actions.Count);
        Assert.Contains(AuditAction.WALLET_DEPOSIT, actions);
        Assert.Contains(AuditAction.WALLET_WITHDRAW, actions);
        Assert.Contains(AuditAction.WALLET_ESCROW_LOCK, actions);
        Assert.Contains(AuditAction.WALLET_ESCROW_RELEASE, actions);
        Assert.Contains(AuditAction.WALLET_REFUND, actions);
        Assert.Contains(AuditAction.COLD_WALLET_TRANSFER_INITIATED, actions);
    }

    [Fact]
    public void ActionsInCategory_ADMIN_ACTION_Returns_Sixteen_Admin_Actions()
    {
        var actions = AuditLogCategoryMap.ActionsInCategory(
            AuditLogCategoryMap.Categories.AdminAction);

        // 7 pre-T54 + 4 fraud-flag (T54) + 3 admin tx lifecycle (T59)
        // + 1 bot recovery triage (T103b-2) + 1 maintenance toggle (WP7) = 16.
        Assert.Equal(16, actions.Count);
        Assert.Contains(AuditAction.SYSTEM_SETTING_CHANGED, actions);
        Assert.Contains(AuditAction.REFUND_BLOCKED, actions);
        Assert.Contains(AuditAction.FRAUD_FLAG_CREATED, actions);
        Assert.Contains(AuditAction.FRAUD_FLAG_APPROVED, actions);
        Assert.Contains(AuditAction.FRAUD_FLAG_REJECTED, actions);
        Assert.Contains(AuditAction.FRAUD_FLAG_AUTO_HOLD, actions);
        Assert.Contains(AuditAction.TRANSACTION_CANCELLED_ADMIN, actions);
        Assert.Contains(AuditAction.EMERGENCY_HOLD_APPLIED, actions);
        Assert.Contains(AuditAction.EMERGENCY_HOLD_RELEASED, actions);
        Assert.Contains(AuditAction.BOT_RECOVERY_UPDATED, actions);
        Assert.Contains(AuditAction.MAINTENANCE_MODE_CHANGED, actions);
    }

    [Fact]
    public void ActionsInCategory_SECURITY_EVENT_Returns_Wallet_Address_Changed_Bot_Status_Reconciliation_HotWalletBreach_And_Sanctions()
    {
        var actions = AuditLogCategoryMap.ActionsInCategory(
            AuditLogCategoryMap.Categories.SecurityEvent);

        // Ordering mirrors the dictionary insertion order in
        // AuditLogCategoryMap: WALLET_ADDRESS_CHANGED (initial) →
        // BOT_STATUS_CHANGED (T69) → BOT_RECOVERY_ITEM_CREATED (T103b-2) →
        // RECONCILIATION_MISMATCH (T76) → HOT_WALLET_THRESHOLD_BREACHED (T77) →
        // SANCTIONS_LIST_ADDRESS_ADDED / SANCTIONS_LIST_ADDRESS_REMOVED (T82).
        Assert.Equal(
            new[]
            {
                AuditAction.WALLET_ADDRESS_CHANGED,
                AuditAction.BOT_STATUS_CHANGED,
                AuditAction.BOT_RECOVERY_ITEM_CREATED,
                AuditAction.RECONCILIATION_MISMATCH,
                AuditAction.HOT_WALLET_THRESHOLD_BREACHED,
                AuditAction.SANCTIONS_LIST_ADDRESS_ADDED,
                AuditAction.SANCTIONS_LIST_ADDRESS_REMOVED,
            },
            actions);
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
