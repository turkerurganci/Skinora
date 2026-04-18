using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Notifications.Domain.Entities;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Notifications.Tests.Integration;

/// <summary>
/// Integration tests for UserNotificationPreference entity (T23).
/// Verifies CRUD, filtered unique constraints, soft delete, and FK enforcement
/// against a real SQL Server instance via TestContainers.
/// </summary>
public class UserNotificationPreferenceEntityTests : IntegrationTestBase
{
    static UserNotificationPreferenceEntityTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        NotificationsModuleDbRegistration.RegisterNotificationsModule();
    }

    private User _user1 = null!;
    private User _user2 = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _user1 = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000001",
            SteamDisplayName = "User1"
        };
        _user2 = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000002",
            SteamDisplayName = "User2"
        };
        context.Set<User>().AddRange(_user1, _user2);
        await context.SaveChangesAsync();
    }

    private UserNotificationPreference CreateValid(
        Guid? userId = null,
        NotificationChannel channel = NotificationChannel.EMAIL,
        string? externalId = "user@example.com")
    {
        return new UserNotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = userId ?? _user1.Id,
            Channel = channel,
            IsEnabled = true,
            ExternalId = externalId
        };
    }

    // ========== CRUD Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UserNotificationPreference_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var pref = CreateValid();
        pref.VerifiedAt = DateTime.UtcNow;

        // Act
        Context.Set<UserNotificationPreference>().Add(pref);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<UserNotificationPreference>().FirstAsync(p => p.Id == pref.Id);

        // Assert
        Assert.Equal(_user1.Id, loaded.UserId);
        Assert.Equal(NotificationChannel.EMAIL, loaded.Channel);
        Assert.True(loaded.IsEnabled);
        Assert.Equal("user@example.com", loaded.ExternalId);
        Assert.NotNull(loaded.VerifiedAt);
        Assert.False(loaded.IsDeleted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UserNotificationPreference_Update_EnableChannel()
    {
        // Arrange
        var pref = CreateValid();
        pref.IsEnabled = false;
        Context.Set<UserNotificationPreference>().Add(pref);
        await Context.SaveChangesAsync();

        // Act
        var tracked = await Context.Set<UserNotificationPreference>().FirstAsync(p => p.Id == pref.Id);
        tracked.IsEnabled = true;
        tracked.VerifiedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<UserNotificationPreference>().FirstAsync(p => p.Id == pref.Id);

        // Assert
        Assert.True(loaded.IsEnabled);
        Assert.NotNull(loaded.VerifiedAt);
    }

    // ========== Soft Delete Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UserNotificationPreference_SoftDelete_FilteredByDefault()
    {
        // Arrange
        var pref = CreateValid();
        pref.IsDeleted = true;
        pref.DeletedAt = DateTime.UtcNow;
        Context.Set<UserNotificationPreference>().Add(pref);
        await Context.SaveChangesAsync();

        // Act
        await using var readCtx = CreateContext();
        var filtered = await readCtx.Set<UserNotificationPreference>().Where(p => p.Id == pref.Id).ToListAsync();
        var unfiltered = await readCtx.Set<UserNotificationPreference>().IgnoreQueryFilters().Where(p => p.Id == pref.Id).ToListAsync();

        // Assert
        Assert.Empty(filtered);
        Assert.Single(unfiltered);
    }

    // ========== Unique Constraint: UserId + Channel (filtered) ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UserNotificationPreference_UserId_Channel_Unique_Rejects_Duplicate_Active()
    {
        // Arrange — first active EMAIL preference
        var pref1 = CreateValid(channel: NotificationChannel.EMAIL);
        Context.Set<UserNotificationPreference>().Add(pref1);
        await Context.SaveChangesAsync();

        // Act — second active EMAIL preference for same user
        await using var ctx2 = CreateContext();
        var pref2 = CreateValid(channel: NotificationChannel.EMAIL);
        pref2.ExternalId = "other@example.com";
        ctx2.Set<UserNotificationPreference>().Add(pref2);

        // Assert
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        Assert.Contains("UQ_UserNotificationPreferences_UserId_Channel", ex.InnerException?.Message ?? ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UserNotificationPreference_UserId_Channel_Unique_Allows_After_SoftDelete()
    {
        // Arrange — soft-deleted EMAIL preference
        var pref1 = CreateValid(channel: NotificationChannel.EMAIL);
        pref1.IsDeleted = true;
        pref1.DeletedAt = DateTime.UtcNow;
        Context.Set<UserNotificationPreference>().Add(pref1);
        await Context.SaveChangesAsync();

        // Act — new active EMAIL preference for same user (allowed because first is soft-deleted)
        var pref2 = CreateValid(channel: NotificationChannel.EMAIL);
        pref2.ExternalId = "new@example.com";
        Context.Set<UserNotificationPreference>().Add(pref2);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var count = await readCtx.Set<UserNotificationPreference>()
            .IgnoreQueryFilters()
            .Where(p => p.UserId == _user1.Id && p.Channel == NotificationChannel.EMAIL)
            .CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UserNotificationPreference_UserId_Channel_Unique_Allows_DifferentChannels()
    {
        // Arrange — EMAIL preference
        var pref1 = CreateValid(channel: NotificationChannel.EMAIL);
        Context.Set<UserNotificationPreference>().Add(pref1);
        await Context.SaveChangesAsync();

        // Act — TELEGRAM preference for same user
        var pref2 = CreateValid(channel: NotificationChannel.TELEGRAM);
        pref2.ExternalId = "123456789";
        Context.Set<UserNotificationPreference>().Add(pref2);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var count = await readCtx.Set<UserNotificationPreference>()
            .Where(p => p.UserId == _user1.Id)
            .CountAsync();
        Assert.Equal(2, count);
    }

    // ========== Unique Constraint: Channel + ExternalId (filtered) ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UserNotificationPreference_Channel_ExternalId_Unique_Rejects_Duplicate_Active()
    {
        // Arrange — user1 has EMAIL with user@example.com
        var pref1 = CreateValid(userId: _user1.Id, channel: NotificationChannel.EMAIL, externalId: "shared@example.com");
        Context.Set<UserNotificationPreference>().Add(pref1);
        await Context.SaveChangesAsync();

        // Act — user2 tries same EMAIL + ExternalId
        await using var ctx2 = CreateContext();
        var pref2 = CreateValid(userId: _user2.Id, channel: NotificationChannel.EMAIL, externalId: "shared@example.com");
        ctx2.Set<UserNotificationPreference>().Add(pref2);

        // Assert
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        Assert.Contains("UQ_UserNotificationPreferences_Channel_ExternalId", ex.InnerException?.Message ?? ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UserNotificationPreference_Channel_ExternalId_Unique_Allows_After_SoftDelete()
    {
        // Arrange — user1 has soft-deleted EMAIL with shared@example.com
        var pref1 = CreateValid(userId: _user1.Id, channel: NotificationChannel.EMAIL, externalId: "shared@example.com");
        pref1.IsDeleted = true;
        pref1.DeletedAt = DateTime.UtcNow;
        Context.Set<UserNotificationPreference>().Add(pref1);
        await Context.SaveChangesAsync();

        // Act — user2 uses same ExternalId (allowed because first is soft-deleted)
        var pref2 = CreateValid(userId: _user2.Id, channel: NotificationChannel.EMAIL, externalId: "shared@example.com");
        Context.Set<UserNotificationPreference>().Add(pref2);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var count = await readCtx.Set<UserNotificationPreference>()
            .IgnoreQueryFilters()
            .Where(p => p.Channel == NotificationChannel.EMAIL && p.ExternalId == "shared@example.com")
            .CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UserNotificationPreference_Channel_ExternalId_Unique_Allows_Null_ExternalId()
    {
        // Arrange — user1 EMAIL with null ExternalId
        var pref1 = CreateValid(userId: _user1.Id, channel: NotificationChannel.EMAIL, externalId: null);
        Context.Set<UserNotificationPreference>().Add(pref1);
        await Context.SaveChangesAsync();

        // Act — user2 EMAIL with null ExternalId (NULLs don't conflict in filtered unique)
        await using var ctx2 = CreateContext();
        var pref2 = CreateValid(userId: _user2.Id, channel: NotificationChannel.EMAIL, externalId: null);
        ctx2.Set<UserNotificationPreference>().Add(pref2);
        await ctx2.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var count = await readCtx.Set<UserNotificationPreference>()
            .Where(p => p.Channel == NotificationChannel.EMAIL && p.ExternalId == null)
            .CountAsync();
        Assert.Equal(2, count);
    }

    // ========== FK Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UserNotificationPreference_InvalidUserId_Rejected()
    {
        // Arrange
        await using var ctx = CreateContext();
        var pref = CreateValid();
        pref.UserId = Guid.NewGuid(); // non-existent
        ctx.Set<UserNotificationPreference>().Add(pref);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
