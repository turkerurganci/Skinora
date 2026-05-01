using Microsoft.EntityFrameworkCore;
using Skinora.Admin.Domain.Entities;
using Skinora.Admin.Infrastructure.Persistence;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Configuration;
using Skinora.Auth.Infrastructure.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Auth.Tests.Integration;

public class AdminAuthorityResolverTests : IntegrationTestBase
{
    static AdminAuthorityResolverTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        AuthModuleDbRegistration.RegisterAuthModule();
        AdminModuleDbRegistration.RegisterAdminModule();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResolveAsync_NoAdminUserRole_ReturnsUserAndEmptyPermissions()
    {
        var user = await CreateUserAsync();
        var sut = new AdminAuthorityResolver(Context);

        var authority = await sut.ResolveAsync(user.Id, default);

        Assert.Equal(AuthRoles.User, authority.Role);
        Assert.Empty(authority.Permissions);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResolveAsync_SuperAdminRole_ReturnsSuperAdminAndEmptyPermissions()
    {
        var user = await CreateUserAsync();
        var role = await CreateRoleAsync(
            "Süper Admin", isSuperAdmin: true, permissions: ["VIEW_FLAGS"]);
        await AssignRoleAsync(user.Id, role.Id);

        var sut = new AdminAuthorityResolver(Context);
        var authority = await sut.ResolveAsync(user.Id, default);

        Assert.Equal(AuthRoles.SuperAdmin, authority.Role);
        // Even though the role has a permission row, the resolver short-circuits
        // because the handler bypasses every requirement on super_admin.
        Assert.Empty(authority.Permissions);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResolveAsync_RegularAdminRole_ReturnsAdminAndOrderedPermissions()
    {
        var user = await CreateUserAsync();
        var role = await CreateRoleAsync(
            "Flag Yöneticisi",
            isSuperAdmin: false,
            permissions: ["VIEW_AUDIT_LOG", "MANAGE_FLAGS", "VIEW_FLAGS"]);
        await AssignRoleAsync(user.Id, role.Id);

        var sut = new AdminAuthorityResolver(Context);
        var authority = await sut.ResolveAsync(user.Id, default);

        Assert.Equal(AuthRoles.Admin, authority.Role);
        // The resolver orders by Permission key for deterministic JWT output.
        Assert.Equal(new[] { "MANAGE_FLAGS", "VIEW_AUDIT_LOG", "VIEW_FLAGS" }, authority.Permissions);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResolveAsync_SoftDeletedUserRole_FallsBackToUser()
    {
        var user = await CreateUserAsync();
        var role = await CreateRoleAsync("Eski Rol", isSuperAdmin: false, permissions: ["VIEW_FLAGS"]);
        var assignment = await AssignRoleAsync(user.Id, role.Id);

        // Soft-delete the assignment — global query filter should hide it from
        // the resolver and demote the user back to "user".
        var ur = await Context.Set<AdminUserRole>()
            .FirstAsync(r => r.Id == assignment.Id);
        ur.IsDeleted = true;
        ur.DeletedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync();

        var sut = new AdminAuthorityResolver(Context);
        var authority = await sut.ResolveAsync(user.Id, default);

        Assert.Equal(AuthRoles.User, authority.Role);
        Assert.Empty(authority.Permissions);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ResolveAsync_PermissionRevokedAfterAssignment_NewLookupReturnsRemainingPermissions()
    {
        // Validates the "dynamic role groups" requirement of T40 — a permission
        // mutation in the DB is reflected by the next ResolveAsync call (i.e.
        // by the next access-token issuance / refresh).
        var user = await CreateUserAsync();
        var role = await CreateRoleAsync(
            "İşlem Denetçisi",
            isSuperAdmin: false,
            permissions: ["VIEW_TRANSACTIONS", "VIEW_FLAGS"]);
        await AssignRoleAsync(user.Id, role.Id);

        var sut = new AdminAuthorityResolver(Context);
        var before = await sut.ResolveAsync(user.Id, default);
        Assert.Contains("VIEW_FLAGS", before.Permissions);

        // Revoke VIEW_FLAGS via soft-delete (mirrors the real AD13 update path).
        var revoked = await Context.Set<AdminRolePermission>()
            .FirstAsync(p => p.AdminRoleId == role.Id && p.Permission == "VIEW_FLAGS");
        revoked.IsDeleted = true;
        revoked.DeletedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync();

        await using var freshContext = CreateContext();
        var freshSut = new AdminAuthorityResolver(freshContext);
        var after = await freshSut.ResolveAsync(user.Id, default);

        Assert.Equal(new[] { "VIEW_TRANSACTIONS" }, after.Permissions);
    }

    private async Task<User> CreateUserAsync()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = $"7656119{Random.Shared.Next(100_000, 999_999):D6}{Random.Shared.Next(0, 9):D1}",
            SteamDisplayName = "Tester",
            PreferredLanguage = "en",
        };
        Context.Set<User>().Add(user);
        await Context.SaveChangesAsync();
        return user;
    }

    private async Task<AdminRole> CreateRoleAsync(
        string name, bool isSuperAdmin, IReadOnlyList<string> permissions)
    {
        var role = new AdminRole
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsSuperAdmin = isSuperAdmin,
        };
        Context.Set<AdminRole>().Add(role);

        foreach (var key in permissions)
        {
            Context.Set<AdminRolePermission>().Add(new AdminRolePermission
            {
                Id = Guid.NewGuid(),
                AdminRoleId = role.Id,
                Permission = key,
            });
        }

        await Context.SaveChangesAsync();
        return role;
    }

    private async Task<AdminUserRole> AssignRoleAsync(Guid userId, Guid roleId)
    {
        var assignment = new AdminUserRole
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AdminRoleId = roleId,
            AssignedAt = DateTime.UtcNow,
        };
        Context.Set<AdminUserRole>().Add(assignment);
        await Context.SaveChangesAsync();
        return assignment;
    }
}
