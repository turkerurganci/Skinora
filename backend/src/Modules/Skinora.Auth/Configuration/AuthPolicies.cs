namespace Skinora.Auth.Configuration;

/// <summary>
/// Authorization policy names used throughout the application.
/// </summary>
public static class AuthPolicies
{
    // --- User policies ---
    public const string Authenticated = "Authenticated";

    // --- Admin policies ---
    public const string AdminAccess = "AdminAccess";
    public const string SuperAdmin = "SuperAdmin";

    // --- Permission-based policies (dynamic, registered per permission) ---
    public const string PermissionPrefix = "Permission:";
}

/// <summary>
/// Custom JWT claim types used by the platform.
/// </summary>
public static class AuthClaimTypes
{
    public const string UserId = "sub";
    public const string SteamId = "steam_id";
    public const string Role = "role";
    public const string Permission = "permission";
    public const string TokenVersion = "token_ver";
}

/// <summary>
/// Role values stored in JWT claims.
/// </summary>
public static class AuthRoles
{
    public const string User = "user";
    public const string Admin = "admin";
    public const string SuperAdmin = "super_admin";
}
