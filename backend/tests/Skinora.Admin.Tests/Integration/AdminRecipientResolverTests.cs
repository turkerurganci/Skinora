using Skinora.Admin.Application.Notifications;
using Skinora.Admin.Domain.Entities;
using Skinora.Admin.Infrastructure.Persistence;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Admin.Tests.Integration;

/// <summary>
/// WP8 — integration coverage for <see cref="AdminRecipientResolver"/>: the
/// distinct set of users holding an active admin role, deduped across multiple
/// roles and excluding soft-deleted assignments and non-admins.
/// </summary>
public class AdminRecipientResolverTests : IntegrationTestBase
{
    static AdminRecipientResolverTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        AdminModuleDbRegistration.RegisterAdminModule();
    }

    private User _adminA = null!;
    private User _adminB = null!;
    private User _nonAdmin = null!;
    private AdminRole _role1 = null!;
    private AdminRole _role2 = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _adminA = new User { Id = Guid.NewGuid(), SteamId = "76561198000000301", SteamDisplayName = "AdminA" };
        _adminB = new User { Id = Guid.NewGuid(), SteamId = "76561198000000302", SteamDisplayName = "AdminB" };
        _nonAdmin = new User { Id = Guid.NewGuid(), SteamId = "76561198000000303", SteamDisplayName = "NonAdmin" };
        context.Set<User>().AddRange(_adminA, _adminB, _nonAdmin);

        _role1 = new AdminRole { Id = Guid.NewGuid(), Name = "Support", IsSuperAdmin = false };
        _role2 = new AdminRole { Id = Guid.NewGuid(), Name = "Finance", IsSuperAdmin = false };
        context.Set<AdminRole>().AddRange(_role1, _role2);

        await context.SaveChangesAsync();
    }

    private AdminUserRole Assignment(Guid userId, Guid roleId, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        AdminRoleId = roleId,
        AssignedAt = DateTime.UtcNow,
        IsDeleted = deleted,
        DeletedAt = deleted ? DateTime.UtcNow : null,
    };

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAdminUserIdsAsync_ReturnsDistinctActiveAdmins_ExcludingDeletedAndNonAdmins()
    {
        // adminA holds two roles (must appear once); adminB one role; the
        // non-admin's only assignment is soft-deleted (must not surface).
        Context.Set<AdminUserRole>().AddRange(
            Assignment(_adminA.Id, _role1.Id),
            Assignment(_adminA.Id, _role2.Id),
            Assignment(_adminB.Id, _role1.Id),
            Assignment(_nonAdmin.Id, _role2.Id, deleted: true));
        await Context.SaveChangesAsync();

        var sut = new AdminRecipientResolver(CreateContext());
        var ids = await sut.GetAdminUserIdsAsync(CancellationToken.None);

        Assert.Equal(2, ids.Count);
        Assert.Contains(_adminA.Id, ids);
        Assert.Contains(_adminB.Id, ids);
        Assert.DoesNotContain(_nonAdmin.Id, ids);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetAdminUserIdsAsync_WithNoAdmins_ReturnsEmpty()
    {
        var sut = new AdminRecipientResolver(CreateContext());
        var ids = await sut.GetAdminUserIdsAsync(CancellationToken.None);

        Assert.Empty(ids);
    }
}
