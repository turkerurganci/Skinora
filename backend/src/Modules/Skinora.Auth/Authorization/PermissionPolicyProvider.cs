using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Skinora.Auth.Configuration;

namespace Skinora.Auth.Authorization;

/// <summary>
/// Dynamically creates authorization policies for permission-based access control.
/// Any policy name starting with "Permission:" is resolved to a <see cref="PermissionRequirement"/>.
/// </summary>
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
            var permission = policyName[AuthPolicies.PermissionPrefix.Length..];

            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();

            return policy;
        }

        return await _fallback.GetPolicyAsync(policyName);
    }
}
