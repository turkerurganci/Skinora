using Microsoft.AspNetCore.Authorization;

namespace Skinora.Auth.Authorization;

/// <summary>
/// Requirement that checks whether the user has a specific permission claim.
/// Used by permission-based policies (e.g. [Authorize(Policy = "Permission:ManageUsers")]).
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}
