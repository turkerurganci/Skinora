using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Skinora.Auth.Configuration;

namespace Skinora.Auth.Authorization;

/// <summary>
/// Dynamically creates authorization policies for permission-based access control.
/// Any policy name starting with "Permission:" is resolved to a <see cref="PermissionRequirement"/>.
/// </summary>
/// <remarks>
/// The suffix may list SEVERAL permission keys separated by commas
/// (<c>"Permission:VIEW_USERS,MANAGE_ROLES"</c>), which builds an any-of
/// requirement. Blank entries are dropped so a stray comma cannot silently
/// widen a policy to "no permission required" — a suffix that yields no key at
/// all falls through to the default provider and therefore fails closed rather
/// than authorizing everyone.
/// </remarks>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
        => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
        => _fallback.GetFallbackPolicyAsync();

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(AuthPolicies.PermissionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var permissions = policyName[AuthPolicies.PermissionPrefix.Length..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (permissions.Length > 0)
            {
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permissions))
                    .Build();

                return policy;
            }
        }

        return await _fallback.GetPolicyAsync(policyName);
    }
}
