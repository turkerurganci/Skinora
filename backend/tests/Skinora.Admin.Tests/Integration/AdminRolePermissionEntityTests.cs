using Microsoft.EntityFrameworkCore;
using Skinora.Admin.Domain.Entities;
using Skinora.Admin.Infrastructure.Persistence;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;

namespace Skinora.Admin.Tests.Integration;

/// <summary>
/// Integration tests for AdminRolePermission entity (T24).
/// Verifies CRUD, filtered unique (AdminRoleId + Permission), FK enforcement,
/// and soft delete against a real SQL Server instance via TestContainers.
/// </summary>
public class AdminRolePermissionEntityTests : IntegrationTestBase
{
    static AdminRolePermissionEntityTests()
    {
        AdminModuleDbRegistration.RegisterAdminModule();
    }

    private AdminRole _role = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _role = new AdminRole
        {
            Id = Guid.NewGuid(),
            Name = "Moderator",
            IsSuperAdmin = false
        };
        context.Set<AdminRole>().Add(_role);
        await context.SaveChangesAsync();
    }

    private AdminRolePermission CreateValid(
        Guid? roleId = null,
        string permission = "transactions.view")
    {
        return new AdminRolePermission
        {
            Id = Guid.NewGuid(),
            AdminRoleId = roleId ?? _role.Id,
            Permission = permission
        };
    }

    // ========== CRUD Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminRolePermission_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var perm = CreateValid();

        // Act
        Context.Set<AdminRolePermission>().Add(perm);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<AdminRolePermission>().FirstAsync(p => p.Id == perm.Id);

        // Assert
        Assert.Equal(_role.Id, loaded.AdminRoleId);
        Assert.Equal("transactions.view", loaded.Permission);
        Assert.False(loaded.IsDeleted);
    }

    // ========== Unique Constraint Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminRolePermission_DuplicatePermission_SameRole_Rejected()
    {
        // Arrange
        var first = CreateValid(permission: "flags.review");
        Context.Set<AdminRolePermission>().Add(first);
        await Context.SaveChangesAsync();

        await using var ctx = CreateContext();
        var duplicate = CreateValid(permission: "flags.review");
        ctx.Set<AdminRolePermission>().Add(duplicate);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminRolePermission_SamePermission_DifferentRole_Allowed()
    {
        // Arrange
        var otherRole = new AdminRole
        {
            Id = Guid.NewGuid(),
            Name = "Auditor",
            IsSuperAdmin = false
        };
        Context.Set<AdminRole>().Add(otherRole);
        await Context.SaveChangesAsync();

        Context.Set<AdminRolePermission>().Add(CreateValid(permission: "audit.read"));
        Context.Set<AdminRolePermission>().Add(CreateValid(roleId: otherRole.Id, permission: "audit.read"));

        // Act
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var count = await readCtx.Set<AdminRolePermission>()
            .CountAsync(p => p.Permission == "audit.read");
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminRolePermission_DuplicatePermission_AfterSoftDelete_Allowed()
    {
        // Filtered unique (WHERE IsDeleted = 0) — soft-deleted rows do not
        // participate in uniqueness.
        var first = CreateValid(permission: "users.manage");
        first.IsDeleted = true;
        first.DeletedAt = DateTime.UtcNow;
        Context.Set<AdminRolePermission>().Add(first);
        await Context.SaveChangesAsync();

        await using var ctx = CreateContext();
        var fresh = CreateValid(permission: "users.manage");
        ctx.Set<AdminRolePermission>().Add(fresh);
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var active = await readCtx.Set<AdminRolePermission>()
            .CountAsync(p => p.Permission == "users.manage");
        Assert.Equal(1, active);
    }

    // ========== FK Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminRolePermission_InvalidRoleId_Rejected()
    {
        // Arrange
        await using var ctx = CreateContext();
        var perm = CreateValid(roleId: Guid.NewGuid()); // non-existent
        ctx.Set<AdminRolePermission>().Add(perm);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    // ========== Soft Delete Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminRolePermission_SoftDelete_FilteredByDefault()
    {
        // Arrange
        var perm = CreateValid(permission: "temp.perm");
        perm.IsDeleted = true;
        perm.DeletedAt = DateTime.UtcNow;
        Context.Set<AdminRolePermission>().Add(perm);
        await Context.SaveChangesAsync();

        // Act
        await using var readCtx = CreateContext();
        var filtered = await readCtx.Set<AdminRolePermission>().Where(p => p.Id == perm.Id).ToListAsync();
        var unfiltered = await readCtx.Set<AdminRolePermission>().IgnoreQueryFilters().Where(p => p.Id == perm.Id).ToListAsync();

        // Assert
        Assert.Empty(filtered);
        Assert.Single(unfiltered);
    }
}
