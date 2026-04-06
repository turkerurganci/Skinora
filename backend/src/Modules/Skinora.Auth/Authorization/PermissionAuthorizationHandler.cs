using Microsoft.AspNetCore.Authorization;
using Skinora.Auth.Configuration;

namespace Skinora.Auth.Authorization;

/// <summary>
/// Handles <see cref="PermissionRequirement"/> by checking the user's permission claims.
/// Super admins automatically satisfy all permission requirements.
/// </summary>
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

        // Check for the specific permission claim
        if (context.User.HasClaim(AuthClaimTypes.Permission, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
