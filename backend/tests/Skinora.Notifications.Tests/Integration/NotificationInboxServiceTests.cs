using Microsoft.EntityFrameworkCore;
using Skinora.Notifications.Application.Inbox;
using Skinora.Notifications.Domain.Entities;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Notifications.Tests.TestSupport;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Notifications.Tests.Integration;

/// <summary>
/// T62 coverage for the realtime side-effect on read-state mutations
/// (07 §11.2 RT2). MarkAllRead pushes a definitive 0; MarkRead recomputes
/// the live unread count and pushes only when the row actually changed.
/// </summary>
public class NotificationInboxServiceTests : IntegrationTestBase
{
    static NotificationInboxServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        NotificationsModuleDbRegistration.RegisterNotificationsModule();
    }

    private User _user = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _user = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000301",
            SteamDisplayName = "InboxTester",
        };
        context.Set<User>().Add(_user);
        await context.SaveChangesAsync();
    }

    private (NotificationInboxService Service, RecordingNotificationRealtimePublisher Publisher) CreateSut()
    {
        var publisher = new RecordingNotificationRealtimePublisher();
        var service = new NotificationInboxService(Context, publisher);
        return (service, publisher);
    }

    private async Task<Notification> SeedNotificationAsync(bool isRead)
    {
        var n = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = _user.Id,
            Type = NotificationType.PAYMENT_RECEIVED,
            Title = "Ödeme",
            Body = "body",
            IsRead = isRead,
            ReadAt = isRead ? DateTime.UtcNow : (DateTime?)null,
        };
        Context.Set<Notification>().Add(n);
        await Context.SaveChangesAsync();
        return n;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MarkAllRead_PushesUnreadCountChanged_WithZero()
    {
        await SeedNotificationAsync(isRead: false);
        await SeedNotificationAsync(isRead: false);

        var (service, publisher) = CreateSut();

        var changed = await service.MarkAllReadAsync(_user.Id, CancellationToken.None);

        Assert.Equal(2, changed);
        var call = Assert.Single(publisher.Calls);
        Assert.Equal("UnreadCountChanged", call.Method);
        Assert.Equal(_user.Id, call.UserId);
        var payload = Assert.IsType<NotificationRealtimePayloads.UnreadCountChanged>(call.Payload);
        Assert.Equal(0, payload.UnreadCount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MarkAllRead_NoUnread_DoesNotPush()
    {
        await SeedNotificationAsync(isRead: true);

        var (service, publisher) = CreateSut();

        var changed = await service.MarkAllReadAsync(_user.Id, CancellationToken.None);

        Assert.Equal(0, changed);
        Assert.Empty(publisher.Calls);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MarkRead_FirstRead_PushesUnreadCountChanged_WithFreshCount()
    {
        var unread1 = await SeedNotificationAsync(isRead: false);
        await SeedNotificationAsync(isRead: false); // remains unread

        var (service, publisher) = CreateSut();

        var outcome = await service.MarkReadAsync(_user.Id, unread1.Id, CancellationToken.None);

        Assert.Equal(MarkReadOutcome.Success, outcome);
        var call = Assert.Single(publisher.Calls);
        Assert.Equal("UnreadCountChanged", call.Method);
        var payload = Assert.IsType<NotificationRealtimePayloads.UnreadCountChanged>(call.Payload);
        Assert.Equal(1, payload.UnreadCount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MarkRead_AlreadyRead_DoesNotPush()
    {
        var alreadyRead = await SeedNotificationAsync(isRead: true);

        var (service, publisher) = CreateSut();

        var outcome = await service.MarkReadAsync(_user.Id, alreadyRead.Id, CancellationToken.None);

        Assert.Equal(MarkReadOutcome.Success, outcome);
        Assert.Empty(publisher.Calls);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MarkRead_NotFound_DoesNotPush()
    {
        var (service, publisher) = CreateSut();

        var outcome = await service.MarkReadAsync(_user.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(MarkReadOutcome.NotFound, outcome);
        Assert.Empty(publisher.Calls);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MarkRead_ForeignNotification_ReturnsForbiddenAndDoesNotPush()
    {
        var stranger = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000302",
            SteamDisplayName = "Stranger",
        };
        Context.Set<User>().Add(stranger);
        await Context.SaveChangesAsync();

        var foreignNotification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = stranger.Id,
            Type = NotificationType.PAYMENT_RECEIVED,
            Title = "stranger",
            Body = "body",
            IsRead = false,
        };
        Context.Set<Notification>().Add(foreignNotification);
        await Context.SaveChangesAsync();

        var (service, publisher) = CreateSut();

        var outcome = await service.MarkReadAsync(_user.Id, foreignNotification.Id, CancellationToken.None);

        Assert.Equal(MarkReadOutcome.Forbidden, outcome);
        Assert.Empty(publisher.Calls);
    }
}
