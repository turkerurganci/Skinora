namespace Skinora.Admin.Application.Users;

/// <summary>
/// Stable error codes for 07 §9.15–§9.18.
/// </summary>
public static class AdminUserErrorCodes
{
    /// <summary>404 — id / steamId resolves to no row.</summary>
    public const string UserNotFound = "USER_NOT_FOUND";

    /// <summary>404 — assignRole request references a non-existent role.</summary>
    public const string RoleNotFound = "ROLE_NOT_FOUND";
}
