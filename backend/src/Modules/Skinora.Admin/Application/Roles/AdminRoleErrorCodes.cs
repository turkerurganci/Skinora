namespace Skinora.Admin.Application.Roles;

/// <summary>
/// Stable error codes for 07 §9.11–§9.14. Mirrors the convention used by
/// <c>NotificationInboxErrorCodes</c> / <c>SettingsErrorCodes</c> so the
/// <see cref="Skinora.Shared.Models.ApiResponse{T}"/> envelope carries
/// human-readable codes rather than ad-hoc strings.
/// </summary>
public static class AdminRoleErrorCodes
{
    /// <summary>
    /// 409 — name collision against <b>any</b> <c>AdminRole</c> row, including a
    /// soft-deleted one. <c>UQ_AdminRoles_Name</c> is unfiltered by design (T24)
    /// so a deleted role's name stays reserved; the check therefore ignores the
    /// soft-delete query filter (T113-AdminRoleNameReuse500). It used to read
    /// "against an active AdminRole", which is what the code did — and why
    /// reusing a deleted role's name returned 500 instead of this code.
    /// </summary>
    public const string RoleNameExists = "ROLE_NAME_EXISTS";

    /// <summary>404 — role id resolves to no active row.</summary>
    public const string RoleNotFound = "ROLE_NOT_FOUND";

    /// <summary>422 — DELETE refused because users are still assigned.</summary>
    public const string RoleHasUsers = "ROLE_HAS_USERS";

    /// <summary>400 — request carries a permission key not in <c>PermissionCatalog</c>.</summary>
    public const string InvalidPermission = "INVALID_PERMISSION";

    /// <summary>400 — generic field validation failure (empty name, etc.).</summary>
    public const string ValidationError = "VALIDATION_ERROR";
}
