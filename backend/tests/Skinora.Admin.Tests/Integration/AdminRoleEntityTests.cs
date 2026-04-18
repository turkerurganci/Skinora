using Microsoft.EntityFrameworkCore;
using Skinora.Admin.Domain.Entities;
using Skinora.Admin.Infrastructure.Persistence;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;

namespace Skinora.Admin.Tests.Integration;

/// <summary>
/// Integration tests for AdminRole entity (T24).
/// Verifies CRUD, unique Name, and soft delete
/// against a real SQL Server instance via TestContainers.
/// </summary>
public class AdminRoleEntityTests : IntegrationTestBase
{
    static AdminRoleEntityTests()
    {
        AdminModuleDbRegistration.RegisterAdminModule();
    }

    private AdminRole CreateValid(string name = "SupportAgent")
    {
        return new AdminRole
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Handles disputes and user queries",
            IsSuperAdmin = false
        };
    }

    // ========== CRUD Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminRole_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var role = CreateValid();

        // Act
        Context.Set<AdminRole>().Add(role);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<AdminRole>().FirstAsync(r => r.Id == role.Id);

        // Assert
        Assert.Equal("SupportAgent", loaded.Name);
        Assert.Equal("Handles disputes and user queries", loaded.Description);
        Assert.False(loaded.IsSuperAdmin);
        Assert.False(loaded.IsDeleted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminRole_Update_Description()
    {
        // Arrange
        var role = CreateValid();
        Context.Set<AdminRole>().Add(role);
        await Context.SaveChangesAsync();

        // Act
        var tracked = await Context.Set<AdminRole>().FirstAsync(r => r.Id == role.Id);
        tracked.Description = "Updated description";
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<AdminRole>().FirstAsync(r => r.Id == role.Id);

        // Assert
        Assert.Equal("Updated description", loaded.Description);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminRole_SuperAdmin_Flag_Persists()
    {
        // Arrange
        var role = CreateValid("SuperAdmin");
        role.IsSuperAdmin = true;

        // Act
        Context.Set<AdminRole>().Add(role);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<AdminRole>().FirstAsync(r => r.Id == role.Id);

        // Assert
        Assert.True(loaded.IsSuperAdmin);
    }

    // ========== Unique Constraint Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminRole_DuplicateName_Rejected()
    {
        // Arrange
        var first = CreateValid("Moderator");
        Context.Set<AdminRole>().Add(first);
        await Context.SaveChangesAsync();

        await using var ctx = CreateContext();
        var duplicate = CreateValid("Moderator");
        ctx.Set<AdminRole>().Add(duplicate);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminRole_DuplicateName_After_SoftDelete_Still_Rejected()
    {
        // AdminRole.Name unique is NOT filtered (06 §5.1) — soft-deleted rows
        // still participate in uniqueness. This prevents accidental role
        // proliferation after a delete.
        var first = CreateValid("Auditor");
        first.IsDeleted = true;
        first.DeletedAt = DateTime.UtcNow;
        Context.Set<AdminRole>().Add(first);
        await Context.SaveChangesAsync();

        await using var ctx = CreateContext();
        var duplicate = CreateValid("Auditor");
        ctx.Set<AdminRole>().Add(duplicate);

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    // ========== Soft Delete Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AdminRole_SoftDelete_FilteredByDefault()
    {
        // Arrange
        var role = CreateValid("TempRole");
        role.IsDeleted = true;
        role.DeletedAt = DateTime.UtcNow;
        Context.Set<AdminRole>().Add(role);
        await Context.SaveChangesAsync();

        // Act
        await using var readCtx = CreateContext();
        var filtered = await readCtx.Set<AdminRole>().Where(r => r.Id == role.Id).ToListAsync();
        var unfiltered = await readCtx.Set<AdminRole>().IgnoreQueryFilters().Where(r => r.Id == role.Id).ToListAsync();

        // Assert
        Assert.Empty(filtered);
        Assert.Single(unfiltered);
    }
}
