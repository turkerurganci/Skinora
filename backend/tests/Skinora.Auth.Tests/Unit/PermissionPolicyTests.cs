using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Skinora.Auth.Authorization;
using Skinora.Auth.Configuration;

namespace Skinora.Auth.Tests.Unit;

/// <summary>
/// Backlog <c>AdminUsersDirectoryPermissionMismatch</c> — the any-of extension
/// to the dynamic <c>Permission:</c> policy, and the guard rails around it.
/// </summary>
/// <remarks>
/// Widening an authorization primitive is the kind of change that is easy to
/// get subtly wrong in the permissive direction, so the tests that matter most
/// here are the negative ones: a policy that names two keys must still refuse
/// someone holding neither, must not become an all-of check, and a malformed
/// suffix must not degrade into "authenticated is enough".
/// </remarks>
[Trait("Category", "Unit")]
public class PermissionPolicyTests
{
    private static PermissionPolicyProvider Provider() =>
        new(Options.Create(new AuthorizationOptions()));

    private static ClaimsPrincipal User(string role, params string[] permissions)
    {
        var claims = new List<Claim> { new(AuthClaimTypes.Role, role) };
        claims.AddRange(permissions.Select(p => new Claim(AuthClaimTypes.Permission, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static async Task<bool> SucceedsAsync(
        PermissionRequirement requirement, ClaimsPrincipal user)
    {
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await new PermissionAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    // ---------- policy name parsing ----------

    [Fact]
    public async Task SingleKeyPolicy_YieldsOnePermission()
    {
        var policy = await Provider().GetPolicyAsync(
            AuthPolicies.PermissionPrefix + "VIEW_USERS");

        var requirement = Assert.IsType<PermissionRequirement>(
            Assert.Single(policy!.Requirements.OfType<PermissionRequirement>()));
        Assert.Equal(["VIEW_USERS"], requirement.Permissions);
    }

    [Fact]
    public async Task CommaSeparatedPolicy_YieldsEveryPermission()
    {
        var policy = await Provider().GetPolicyAsync(
            AuthPolicies.PermissionPrefix + "VIEW_USERS,MANAGE_ROLES");

        var requirement = Assert.Single(
            policy!.Requirements.OfType<PermissionRequirement>());
        Assert.Equal(["VIEW_USERS", "MANAGE_ROLES"], requirement.Permissions);
    }

    [Fact]
    public async Task WhitespaceAndStrayCommas_AreIgnored()
    {
        var policy = await Provider().GetPolicyAsync(
            AuthPolicies.PermissionPrefix + " VIEW_USERS , , MANAGE_ROLES ");

        var requirement = Assert.Single(
            policy!.Requirements.OfType<PermissionRequirement>());
        Assert.Equal(["VIEW_USERS", "MANAGE_ROLES"], requirement.Permissions);
    }

    [Fact]
    public async Task EmptySuffix_DoesNotProduceAnEmptyRequirement()
    {
        // The failure mode worth naming: a requirement with no keys would be
        // satisfied by anyone, turning a typo into an open endpoint. The
        // provider must fall through instead — an unregistered policy name
        // throws at authorization time, which fails closed.
        var policy = await Provider().GetPolicyAsync(AuthPolicies.PermissionPrefix + ",,");

        Assert.Null(policy);
    }

    [Fact]
    public void EmptyRequirement_CannotBeConstructed()
    {
        Assert.Throws<ArgumentException>(() => new PermissionRequirement(Array.Empty<string>()));
    }

    // ---------- handler semantics ----------

    [Fact]
    public async Task AnyOneOfTheKeys_Satisfies()
    {
        var requirement = new PermissionRequirement(["VIEW_USERS", "MANAGE_ROLES"]);

        Assert.True(await SucceedsAsync(requirement, User(AuthRoles.Admin, "VIEW_USERS")));
        Assert.True(await SucceedsAsync(requirement, User(AuthRoles.Admin, "MANAGE_ROLES")));
    }

    [Fact]
    public async Task HoldingNeitherKey_IsRefused()
    {
        var requirement = new PermissionRequirement(["VIEW_USERS", "MANAGE_ROLES"]);

        Assert.False(await SucceedsAsync(requirement, User(AuthRoles.Admin, "VIEW_FLAGS")));
        Assert.False(await SucceedsAsync(requirement, User(AuthRoles.Admin)));
    }

    [Fact]
    public async Task SingleKeyRequirement_IsUnchanged()
    {
        // The widening must not leak into the ~30 endpoints that name one key.
        var requirement = new PermissionRequirement("MANAGE_ROLES");

        Assert.True(await SucceedsAsync(requirement, User(AuthRoles.Admin, "MANAGE_ROLES")));
        Assert.False(await SucceedsAsync(requirement, User(AuthRoles.Admin, "VIEW_USERS")));
    }

    [Fact]
    public async Task SuperAdmin_StillBypasses()
    {
        var requirement = new PermissionRequirement(["VIEW_USERS", "MANAGE_ROLES"]);

        Assert.True(await SucceedsAsync(requirement, User(AuthRoles.SuperAdmin)));
    }
}
