using Skinora.Shared.Enums;

namespace Skinora.Platform.Application.Audit;

/// <summary>
/// Maps every <see cref="AuditAction"/> value to one of the 3 admin-facing
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

            // T54 — fraud flag review actions. Categorised under ADMIN_ACTION
            // because the create/approve/reject/auto-hold trail is consumed by
            // the admin queue (07 §9.2 review surface), even though the actor
            // can be SYSTEM for the auto-detection path.
            [AuditAction.FRAUD_FLAG_CREATED] = Categories.AdminAction,
            [AuditAction.FRAUD_FLAG_APPROVED] = Categories.AdminAction,
            [AuditAction.FRAUD_FLAG_REJECTED] = Categories.AdminAction,
            [AuditAction.FRAUD_FLAG_AUTO_HOLD] = Categories.AdminAction,

            // T59 — admin transaction lifecycle. Direct admin cancel (AD19),
            // emergency hold apply/release (AD19b/c) all surface in the admin
            // queue alongside the other ADMIN_ACTION rows (07 §9.20–§9.22).
            [AuditAction.TRANSACTION_CANCELLED_ADMIN] = Categories.AdminAction,
            [AuditAction.EMERGENCY_HOLD_APPLIED] = Categories.AdminAction,
            [AuditAction.EMERGENCY_HOLD_RELEASED] = Categories.AdminAction,

            // T76 — daily reconciliation discrepancy (05 §3.3). An on-chain
            // vs ledger gap is a custody-integrity alarm: it sits in the
            // security queue so the same operators who watch wallet-address
            // events see it on the same dashboard.
            [AuditAction.RECONCILIATION_MISMATCH] = Categories.SecurityEvent,

            // T77 — admin-initiated hot→cold operational consolidation
            // (05 §3.3). Real fund movement out of the hot wallet — it
            // belongs next to the customer-facing WALLET_* rows in the
            // fund movement queue.
            [AuditAction.COLD_WALLET_TRANSFER_INITIATED] = Categories.FundMovement,

            // T77 — periodic hot wallet balance monitor detected a
            // threshold crossing (05 §3.3). Mirrors RECONCILIATION_MISMATCH
            // shape and audience: a custody-integrity alarm visible on the
            // same admin dashboard.
            [AuditAction.HOT_WALLET_THRESHOLD_BREACHED] = Categories.SecurityEvent,

            // T129 fix round — AD32 settlement clearance (07 §9.22b). An admin
            // releasing a payout the automated check would not release is the
            // same class of decision as DISPUTE_RESOLVED / MANUAL_REFUND, and
            // belongs on the same review queue.
            [AuditAction.SETTLEMENT_CLEARED_ADMIN] = Categories.AdminAction,

            // T82 — sanctions list mutation events (02 §21.1, 07 §9.24–§9.25).
            // Admin AD23 / AD24 adres ekleme / deaktive aksiyonları
            // wallet-address-changed / reconciliation-mismatch ile aynı
            // güvenlik kuyruğunda görünür.
            [AuditAction.SANCTIONS_LIST_ADDRESS_ADDED] = Categories.SecurityEvent,
            [AuditAction.SANCTIONS_LIST_ADDRESS_REMOVED] = Categories.SecurityEvent,

            // WP7 — platform maintenance/outage toggle (AD30/AD31). Entering or
            // leaving maintenance is a deliberate operator action on platform
            // settings, so it sits with SYSTEM_SETTING_CHANGED in the admin queue.
            [AuditAction.MAINTENANCE_MODE_CHANGED] = Categories.AdminAction,

            // WP16 — restart-recovery automatic timeout extension (05 §4.4:536).
            // SYSTEM-driven, but it is the automatic counterpart of the manual
            // MAINTENANCE_MODE_CHANGED downtime handling, so operators review it on
            // the same admin queue.
            [AuditAction.TIMEOUT_AUTO_EXTENDED] = Categories.AdminAction,

            // WP16 — platform health probe outage/recovery (05 §4.4, 02 §3.3).
            // A sidecar going down is an operational integrity alarm, so it sits
            // in the security queue beside the reconciliation/hot-wallet alarms.
            [AuditAction.PLATFORM_OUTAGE_DETECTED] = Categories.SecurityEvent,
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
