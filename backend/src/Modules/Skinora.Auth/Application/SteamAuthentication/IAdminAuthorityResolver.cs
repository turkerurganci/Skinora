namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Resolves the admin role + permissions a user currently holds. Called from
/// <see cref="IAccessTokenGenerator"/> at JWT issuance time so claims reflect
/// the live <c>AdminUserRole → AdminRole → AdminRolePermission</c> chain
/// (06 §3.14–§3.16) rather than a static value.
///
/// 05 §6.2 — policy-based authorization with dynamic role groups (DB-backed
/// permissions). 07 §9 enforces 11 permission keys via the
/// <c>Permission:&lt;KEY&gt;</c> policy provider (T06).
/// </summary>
public interface IAdminAuthorityResolver
{
    Task<AdminAuthority> ResolveAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Result of <see cref="IAdminAuthorityResolver.ResolveAsync"/>. <c>Role</c>
/// is the literal value emitted into the JWT <c>role</c> claim
/// (<see cref="Configuration.AuthRoles"/>); <c>Permissions</c> is empty for
/// super admins (handler bypasses) and non-admin users.
/// </summary>
public sealed record AdminAuthority(string Role, IReadOnlyList<string> Permissions);
