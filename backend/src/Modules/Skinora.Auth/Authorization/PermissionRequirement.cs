using Microsoft.AspNetCore.Authorization;

namespace Skinora.Auth.Authorization;

/// <summary>
/// Requirement that checks whether the user holds a permission claim.
/// Used by permission-based policies (e.g. [Authorize(Policy = "Permission:VIEW_USERS")]).
/// </summary>
/// <remarks>
/// <para>
/// A requirement may name MORE THAN ONE permission, in which case holding
/// <b>any</b> of them satisfies it (see <see cref="PermissionPolicyProvider"/>,
/// which splits a comma-separated policy suffix). Backlog
/// <c>AdminUsersDirectoryPermissionMismatch</c> is the first caller: AD15
/// <c>GET /admin/users</c> is a read-only directory that both the role-assignment
/// surface (<c>MANAGE_ROLES</c>) and the user-detail surface (<c>VIEW_USERS</c>)
/// need as their entry point, while the mutating role endpoints keep
/// <c>MANAGE_ROLES</c> alone.
/// </para>
/// <para>
/// "Any" — not "all" — is the deliberate semantic. An all-of requirement would
/// be a narrowing, and every permission key in this system is already the
/// narrowest gate for its own surface; combining two keys is only ever used to
/// say "either of these roles legitimately reaches this read".
/// </para>
/// </remarks>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// The permission keys that satisfy this requirement. Never empty; holding
    /// any one of them is enough.
    /// </summary>
    public IReadOnlyList<string> Permissions { get; }

    /// <summary>
    /// The single permission key. Kept for the common one-key case and for
    /// diagnostics; when several keys are accepted this is the first of them.
    /// </summary>
    public string Permission => Permissions[0];

    public PermissionRequirement(string permission)
        : this([permission])
    {
    }

    public PermissionRequirement(IReadOnlyList<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (permissions.Count == 0)
            throw new ArgumentException(
                "A permission requirement must name at least one permission.",
                nameof(permissions));

        Permissions = permissions;
    }
}
