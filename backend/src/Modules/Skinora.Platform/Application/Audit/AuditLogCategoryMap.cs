using Skinora.Shared.Enums;

namespace Skinora.Platform.Application.Audit;

/// <summary>
/// Maps the 13 <see cref="AuditAction"/> values to the 3 admin-facing
/// categories surfaced by <c>GET /admin/audit-logs</c> (07 §9.19) and
/// the 06 §2.19 group column.
/// </summary>
public static class AuditLogCategoryMap
{
    /// <summary>API category strings — 07 §9.19 enum.</summary>
    public static class Categories
    {
        public const string FundMovement = "FUND_MOVEMENT";
        public const string AdminAction = "ADMIN_ACTION";
        public const string SecurityEvent = "SECURITY_EVENT";
    }

    private static readonly IReadOnlyDictionary<AuditAction, string> _actionToCategory =
        new Dictionary<AuditAction, string>
        {
            // 06 §2.19 "Fon" group.
            [AuditAction.WALLET_DEPOSIT] = Categories.FundMovement,
            [AuditAction.WALLET_WITHDRAW] = Categories.FundMovement,
            [AuditAction.WALLET_ESCROW_LOCK] = Categories.FundMovement,
            [AuditAction.WALLET_ESCROW_RELEASE] = Categories.FundMovement,
            [AuditAction.WALLET_REFUND] = Categories.FundMovement,

            // 06 §2.19 "Admin" group.
            [AuditAction.DISPUTE_RESOLVED] = Categories.AdminAction,
            [AuditAction.MANUAL_REFUND] = Categories.AdminAction,
            // REFUND_BLOCKED is platform-driven (SYSTEM actor) but it surfaces in
            // the same admin queue as MANUAL_REFUND — the operator decides what
            // to do with the residue. Categorising it under FUND_MOVEMENT would
            // bury it among the high-volume wallet rows and defeat the alert.
            [AuditAction.REFUND_BLOCKED] = Categories.AdminAction,
            [AuditAction.USER_BANNED] = Categories.AdminAction,
            [AuditAction.USER_UNBANNED] = Categories.AdminAction,
            [AuditAction.ROLE_CHANGED] = Categories.AdminAction,
            [AuditAction.SYSTEM_SETTING_CHANGED] = Categories.AdminAction,

            // 06 §2.19 "Güvenlik" group.
            [AuditAction.WALLET_ADDRESS_CHANGED] = Categories.SecurityEvent,
        };

    /// <summary>Returns the API category for the supplied <paramref name="action"/>.</summary>
    public static string CategoryFor(AuditAction action) =>
        _actionToCategory.TryGetValue(action, out var category)
            ? category
            : throw new ArgumentOutOfRangeException(
                nameof(action), action,
                "AuditAction is not mapped to a category — extend AuditLogCategoryMap.");

    /// <summary>
    /// Returns every <see cref="AuditAction"/> belonging to <paramref name="category"/>
    /// (case-sensitive match against the API enum strings). Returns an empty
    /// array when the category is unknown — callers translate that to an empty
    /// result set rather than 400.
    /// </summary>
    public static IReadOnlyList<AuditAction> ActionsInCategory(string category)
    {
        if (string.IsNullOrEmpty(category))
            return Array.Empty<AuditAction>();

        return _actionToCategory
            .Where(kvp => string.Equals(kvp.Value, category, StringComparison.Ordinal))
            .Select(kvp => kvp.Key)
            .ToArray();
    }

    /// <summary>True when <paramref name="category"/> is one of the three valid API values.</summary>
    public static bool IsValidCategory(string? category) =>
        category == Categories.FundMovement
        || category == Categories.AdminAction
        || category == Categories.SecurityEvent;
}
