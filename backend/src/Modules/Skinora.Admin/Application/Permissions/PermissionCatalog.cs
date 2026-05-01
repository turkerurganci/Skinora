namespace Skinora.Admin.Application.Permissions;

/// <summary>
/// Static catalog of the 11 admin permissions defined by 07 §9.11
/// <c>availablePermissions</c> (kept 1:1 with the 04 §8.8 yetki matrix).
/// Single source of truth in code — the AD11 response, role validation
/// (INVALID_PERMISSION) and the Permission constants used by
/// <c>[Authorize(Policy = "Permission:...")]</c> all read from here.
/// </summary>
public static class PermissionCatalog
{
    /// <summary>Stable string identifiers — emitted into JWT permission claims.</summary>
    public static class Keys
    {
        public const string ViewFlags = "VIEW_FLAGS";
        public const string ManageFlags = "MANAGE_FLAGS";
        public const string ViewTransactions = "VIEW_TRANSACTIONS";
        public const string ManageSettings = "MANAGE_SETTINGS";
        public const string ViewSteamAccounts = "VIEW_STEAM_ACCOUNTS";
        public const string ManageSteamRecovery = "MANAGE_STEAM_RECOVERY";
        public const string ViewUsers = "VIEW_USERS";
        public const string ManageRoles = "MANAGE_ROLES";
        public const string ViewAuditLog = "VIEW_AUDIT_LOG";
        public const string CancelTransactions = "CANCEL_TRANSACTIONS";
        public const string EmergencyHold = "EMERGENCY_HOLD";
    }

    /// <summary>
    /// Permission entries in the order documented by 07 §9.11. Frontend
    /// renders the S19 yetki matrix in the order returned here.
    /// </summary>
    public static IReadOnlyList<PermissionEntry> All { get; } =
    [
        new(Keys.ViewFlags, "Flag'leri görüntüle"),
        new(Keys.ManageFlags, "Flag'leri yönet"),
        new(Keys.ViewTransactions, "İşlemleri görüntüle"),
        new(Keys.ManageSettings, "Parametreleri yönet"),
        new(Keys.ViewSteamAccounts, "Steam hesaplarını görüntüle"),
        new(Keys.ManageSteamRecovery, "Steam recovery yönet"),
        new(Keys.ViewUsers, "Kullanıcı detay görüntüle"),
        new(Keys.ManageRoles, "Rolleri yönet"),
        new(Keys.ViewAuditLog, "Audit log görüntüle"),
        new(Keys.CancelTransactions, "İşlemleri iptal et"),
        new(Keys.EmergencyHold, "İşlemleri acil dondurma/kaldırma"),
    ];

    private static readonly HashSet<string> KeySet =
        new(All.Select(p => p.Key), StringComparer.Ordinal);

    /// <summary>Returns true if <paramref name="key"/> is one of the 11 catalog entries.</summary>
    public static bool IsKnown(string key) => KeySet.Contains(key);
}

/// <summary>One entry of <see cref="PermissionCatalog.All"/>.</summary>
public sealed record PermissionEntry(string Key, string Label);
