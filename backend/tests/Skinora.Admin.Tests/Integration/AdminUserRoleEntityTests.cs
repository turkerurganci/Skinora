using Microsoft.EntityFrameworkCore;
using Skinora.Admin.Domain.Entities;
using Skinora.Admin.Infrastructure.Persistence;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Admin.Tests.Integration;

/// <summary>
/// Integration tests for AdminUserRole entity (T24).
/// Verifies CRUD, filtered unique (UserId + AdminRoleId), FK enforcement,
/// and soft delete against a real SQL Server instance via TestContainers.
/// </summary>
public class AdminUserRoleEntityTests : IntegrationTestBase
{
    static AdminUserRoleEntityTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        AdminModuleDbRegistration.RegisterAdminModule();
    }

    private User _user = null!;
    private User _assigner = null!;
    private AdminRole _role = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000001",
            SteamDisplayName = "AssigneeUser"
        };
        _assigner = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000002",
            SteamDisplayName = "AssignerAdmin"
        };
        context.Set<User>().AddRange(_user, _assigner);

        _role = new AdminRole
        {
            Id = Guid.NewGuid(),
            Name = "Moderator",
            IsSuperAdmin = false
        };
        context.Set<AdminRole>().Add(_role);

        await context.SaveChangesAsync();
    }

    private AdminUserRole CreateValid(
        Guid? userId = null,
        Guid? roleId = null,
        Guid? assignedBy = null)
    {
        return new AdminUserRole
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? _user.Id,
            AdminRoleId = roleId ?? _role.Id,
            AssignedAt = DateTime.UtcNow,
            AssignedByAdminId = assignedBy ?? _assigner.Id
        };
    }

    // ========== CRUD Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminUserRole_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var assignment = CreateValid();

        // Act
        Context.Set<AdminUserRole>().Add(assignment);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<AdminUserRole>().FirstAsync(a => a.Id == assignment.Id);

        // Assert
        Assert.Equal(_user.Id, loaded.UserId);
        Assert.Equal(_role.Id, loaded.AdminRoleId);
        Assert.Equal(_assigner.Id, loaded.AssignedByAdminId);
        Assert.NotEqual(default, loaded.AssignedAt);
        Assert.False(loaded.IsDeleted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminUserRole_NullAssignedBy_Accepted()
    {
        // System-assigned roles (e.g., initial super admin bootstrap) may
        // have a null AssignedByAdminId.
        var assignment = CreateValid(assignedBy: null);
        assignment.AssignedByAdminId = null;

        Context.Set<AdminUserRole>().Add(assignment);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<AdminUserRole>().FirstAsync(a => a.Id == assignment.Id);
        Assert.Null(loaded.AssignedByAdminId);
    }

    // ========== Unique Constraint Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminUserRole_DuplicateAssignment_Rejected()
    {
        // Arrange
        var first = CreateValid();
        Context.Set<AdminUserRole>().Add(first);
        await Context.SaveChangesAsync();

        await using var ctx = CreateContext();
        var duplicate = CreateValid();
        ctx.Set<AdminUserRole>().Add(duplicate);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminUserRole_Reassignment_After_SoftDelete_Allowed()
    {
        // Filtered unique (WHERE IsDeleted = 0) — surrogate PK pattern per
        // 06 §3.16 lets the same user be re-assigned the same role after a
        // soft delete.
        var first = CreateValid();
        first.IsDeleted = true;
        first.DeletedAt = DateTime.UtcNow;
        Context.Set<AdminUserRole>().Add(first);
        await Context.SaveChangesAsync();

        await using var ctx = CreateContext();
        var fresh = CreateValid();
        ctx.Set<AdminUserRole>().Add(fresh);
        await ctx.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var activeCount = await readCtx.Set<AdminUserRole>()
            .CountAsync(a => a.UserId == _user.Id && a.AdminRoleId == _role.Id);
        Assert.Equal(1, activeCount);
    }

    // ========== FK Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminUserRole_InvalidUserId_Rejected()
    {
        await using var ctx = CreateContext();
        var assignment = CreateValid(userId: Guid.NewGuid()); // non-existent
        ctx.Set<AdminUserRole>().Add(assignment);

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminUserRole_InvalidRoleId_Rejected()
    {
        await using var ctx = CreateContext();
        var assignment = CreateValid(roleId: Guid.NewGuid()); // non-existent
        ctx.Set<AdminUserRole>().Add(assignment);

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminUserRole_InvalidAssignedByAdminId_Rejected()
    {
        await using var ctx = CreateContext();
        var assignment = CreateValid(assignedBy: Guid.NewGuid()); // non-existent
        ctx.Set<AdminUserRole>().Add(assignment);

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    // ========== Soft Delete Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminUserRole_SoftDelete_FilteredByDefault()
    {
        // Arrange
        var assignment = CreateValid();
        assignment.IsDeleted = true;
        assignment.DeletedAt = DateTime.UtcNow;
        Context.Set<AdminUserRole>().Add(assignment);
        await Context.SaveChangesAsync();

        // Act
        await using var readCtx = CreateContext();
        var filtered = await readCtx.Set<AdminUserRole>().Where(a => a.Id == assignment.Id).ToListAsync();
        var unfiltered = await readCtx.Set<AdminUserRole>().IgnoreQueryFilters().Where(a => a.Id == assignment.Id).ToListAsync();

        // Assert
        Assert.Empty(filtered);
        Assert.Single(unfiltered);
    }
}
