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
/// Integration tests for NotificationDelivery entity (T23).
/// Verifies CRUD, unique constraint, CHECK constraints, and FK enforcement
/// against a real SQL Server instance via TestContainers.
/// </summary>
public class NotificationDeliveryEntityTests : IntegrationTestBase
{
    static NotificationDeliveryEntityTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        NotificationsModuleDbRegistration.RegisterNotificationsModule();
    }

    private User _user = null!;
    private Notification _notification = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000001",
            SteamDisplayName = "TestUser"
        };
        context.Set<User>().Add(_user);
        await context.SaveChangesAsync();

        _notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = _user.Id,
            Type = NotificationType.TRANSACTION_INVITE,
            Title = "Trade invitation",
            Body = "You have a new trade invitation"
        };
        context.Set<Notification>().Add(_notification);
        await context.SaveChangesAsync();
    }

    private NotificationDelivery CreateValid(
        NotificationChannel channel = NotificationChannel.EMAIL,
        DeliveryStatus status = DeliveryStatus.PENDING)
    {
        return new NotificationDelivery
        {
            Id = Guid.NewGuid(),
            NotificationId = _notification.Id,
            Channel = channel,
            TargetExternalId = "user@example.com",
            Status = status
        };
    }

    // ========== CRUD Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NotificationDelivery_Insert_And_Read_RoundTrips()
    {
        // Arrange
        var delivery = CreateValid();

        // Act
        Context.Set<NotificationDelivery>().Add(delivery);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<NotificationDelivery>().FirstAsync(d => d.Id == delivery.Id);

        // Assert
        Assert.Equal(_notification.Id, loaded.NotificationId);
        Assert.Equal(NotificationChannel.EMAIL, loaded.Channel);
        Assert.Equal("user@example.com", loaded.TargetExternalId);
        Assert.Equal(DeliveryStatus.PENDING, loaded.Status);
        Assert.Equal(0, loaded.AttemptCount);
        Assert.Null(loaded.LastError);
        Assert.Null(loaded.SentAt);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NotificationDelivery_Update_Status_To_Sent()
    {
        // Arrange
        var delivery = CreateValid();
        Context.Set<NotificationDelivery>().Add(delivery);
        await Context.SaveChangesAsync();

        // Act
        var tracked = await Context.Set<NotificationDelivery>().FirstAsync(d => d.Id == delivery.Id);
        tracked.Status = DeliveryStatus.SENT;
        tracked.SentAt = DateTime.UtcNow;
        tracked.AttemptCount = 1;
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<NotificationDelivery>().FirstAsync(d => d.Id == delivery.Id);

        // Assert
        Assert.Equal(DeliveryStatus.SENT, loaded.Status);
        Assert.NotNull(loaded.SentAt);
        Assert.Equal(1, loaded.AttemptCount);
    }

    // ========== Unique Constraint Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NotificationDelivery_NotificationId_Channel_Unique_Rejects_Duplicate()
    {
        // Arrange — first EMAIL delivery
        var delivery1 = CreateValid(NotificationChannel.EMAIL);
        Context.Set<NotificationDelivery>().Add(delivery1);
        await Context.SaveChangesAsync();

        // Act — second EMAIL delivery for same notification
        await using var ctx2 = CreateContext();
        var delivery2 = CreateValid(NotificationChannel.EMAIL);
        ctx2.Set<NotificationDelivery>().Add(delivery2);

        // Assert
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
        Assert.Contains("UQ_NotificationDeliveries_NotificationId_Channel", ex.InnerException?.Message ?? ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NotificationDelivery_NotificationId_Channel_Unique_Allows_DifferentChannels()
    {
        // Arrange — EMAIL delivery
        var delivery1 = CreateValid(NotificationChannel.EMAIL);
        delivery1.TargetExternalId = "user@example.com";
        Context.Set<NotificationDelivery>().Add(delivery1);
        await Context.SaveChangesAsync();

        // Act — TELEGRAM delivery for same notification
        var delivery2 = CreateValid(NotificationChannel.TELEGRAM);
        delivery2.TargetExternalId = "123456789";
        Context.Set<NotificationDelivery>().Add(delivery2);
        await Context.SaveChangesAsync();

        // Assert
        await using var readCtx = CreateContext();
        var count = await readCtx.Set<NotificationDelivery>()
            .Where(d => d.NotificationId == _notification.Id)
            .CountAsync();
        Assert.Equal(2, count);
    }

    // ========== CHECK Constraint Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NotificationDelivery_Sent_Without_SentAt_Rejected()
    {
        // Arrange — SENT without SentAt
        await using var ctx = CreateContext();
        var delivery = CreateValid(NotificationChannel.EMAIL, DeliveryStatus.SENT);
        // SentAt deliberately left null
        ctx.Set<NotificationDelivery>().Add(delivery);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("CK_NotificationDeliveries_Sent_SentAt", ex.InnerException?.Message ?? ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NotificationDelivery_Sent_With_SentAt_Accepted()
    {
        // Arrange — SENT with SentAt
        var delivery = CreateValid(NotificationChannel.EMAIL, DeliveryStatus.SENT);
        delivery.SentAt = DateTime.UtcNow;
        delivery.AttemptCount = 1;
        Context.Set<NotificationDelivery>().Add(delivery);

        // Act & Assert — no exception
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<NotificationDelivery>().FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal(DeliveryStatus.SENT, loaded.Status);
        Assert.NotNull(loaded.SentAt);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NotificationDelivery_Failed_Without_LastError_Rejected()
    {
        // Arrange — FAILED without LastError
        await using var ctx = CreateContext();
        var delivery = CreateValid(NotificationChannel.TELEGRAM, DeliveryStatus.FAILED);
        delivery.TargetExternalId = "123456789";
        // LastError deliberately left null
        ctx.Set<NotificationDelivery>().Add(delivery);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("CK_NotificationDeliveries_Failed_LastError", ex.InnerException?.Message ?? ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NotificationDelivery_Failed_With_LastError_Accepted()
    {
        // Arrange — FAILED with LastError
        var delivery = CreateValid(NotificationChannel.TELEGRAM, DeliveryStatus.FAILED);
        delivery.TargetExternalId = "123456789";
        delivery.LastError = "Connection timeout after 3 retries";
        delivery.AttemptCount = 3;
        Context.Set<NotificationDelivery>().Add(delivery);

        // Act & Assert — no exception
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<NotificationDelivery>().FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal(DeliveryStatus.FAILED, loaded.Status);
        Assert.Equal("Connection timeout after 3 retries", loaded.LastError);
    }

    // ========== FK Tests ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NotificationDelivery_InvalidNotificationId_Rejected()
    {
        // Arrange
        await using var ctx = CreateContext();
        var delivery = CreateValid();
        delivery.NotificationId = Guid.NewGuid(); // non-existent
        ctx.Set<NotificationDelivery>().Add(delivery);

        // Act & Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
