namespace Skinora.Admin.Application.Roles;

/// <summary>
/// Stable error codes for 07 §9.11–§9.14. Mirrors the convention used by
/// <c>NotificationInboxErrorCodes</c> / <c>SettingsErrorCodes</c> so the
/// <see cref="Skinora.Shared.Models.ApiResponse{T}"/> envelope carries
/// human-readable codes rather than ad-hoc strings.
/// </summary>
public static class AdminRoleErrorCodes
{
    /// <summary>409 — name collision against an active <c>AdminRole</c>.</summary>
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
