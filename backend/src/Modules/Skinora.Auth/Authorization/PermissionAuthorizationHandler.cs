using Microsoft.AspNetCore.Authorization;
using Skinora.Auth.Configuration;

namespace Skinora.Auth.Authorization;

/// <summary>
/// Handles <see cref="PermissionRequirement"/> by checking the user's permission claims.
/// Super admins automatically satisfy all permission requirements.
/// </summary>
/// <remarks>
/// A requirement that names several permissions is satisfied by holding
/// <b>any</b> one of them (<see cref="PermissionRequirement.Permissions"/>).
/// </remarks>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        // Super admin bypasses all permission checks
        if (context.User.HasClaim(AuthClaimTypes.Role, AuthRoles.SuperAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Any one of the named permission claims satisfies the requirement.
        foreach (var permission in requirement.Permissions)
        {
            if (context.User.HasClaim(AuthClaimTypes.Permission, permission))
            {
                context.Succeed(requirement);
                break;
            }
        }

        return Task.CompletedTask;
    }
}
