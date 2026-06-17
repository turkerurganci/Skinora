using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.Notifications;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Domain.Entities;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Notifications.Resources;
using Skinora.Notifications.Tests.TestSupport;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Notifications.Tests.Integration;

/// <summary>
/// Integration coverage for <see cref="NotificationDispatcher"/> — verifies
/// platform-in-app row, external delivery rows, channel filtering, locale
/// resolution and job enqueue behaviour against a real SQL Server.
/// </summary>
public class NotificationDispatcherTests : IntegrationTestBase
{
    static NotificationDispatcherTests()
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
            SteamId = "76561198000000201",
            SteamDisplayName = "DispatcherTester",
            PreferredLanguage = "tr",
        };
        context.Set<User>().Add(_user);
        await context.SaveChangesAsync();
    }

    private (NotificationDispatcher Dispatcher,
             FakeBackgroundJobScheduler Scheduler,
             RecordingNotificationRealtimePublisher RealtimePublisher) CreateSut()
    {
        var services = new ServiceCollection();
        services.AddLocalization();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var localizer = provider.GetRequiredService<IStringLocalizer<NotificationTemplates>>();

        var resolver = new ResxNotificationTemplateResolver(
            localizer,
            NullLogger<ResxNotificationTemplateResolver>.Instance);
        var scheduler = new FakeBackgroundJobScheduler();
        var realtimePublisher = new RecordingNotificationRealtimePublisher();

        var dispatcher = new NotificationDispatcher(
            Context,
            resolver,
            scheduler,
            realtimePublisher,
            NullLogger<NotificationDispatcher>.Instance);

        return (dispatcher, scheduler, realtimePublisher);
    }

    private async Task SetPreferenceAsync(NotificationChannel channel, bool enabled, string? externalId)
    {
        var pref = new UserNotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = _user.Id,
            Channel = channel,
            IsEnabled = enabled,
            ExternalId = externalId,
            VerifiedAt = externalId is null ? null : DateTime.UtcNow,
        };
        Context.Set<UserNotificationPreference>().Add(pref);
        await Context.SaveChangesAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DispatchAsync_AlwaysWritesPlatformInAppNotification()
    {
        var (dispatcher, _, _) = CreateSut();

        await dispatcher.DispatchAsync(
            new NotificationRequest
            {
                UserId = _user.Id,
                Type = NotificationType.PAYMENT_RECEIVED,
                Parameters = new Dictionary<string, string> { ["Amount"] = "42" },
            },
            CancellationToken.None);

        await Context.SaveChangesAsync();

        await using var verify = CreateContext();
        var notification = await verify.Set<Notification>().SingleAsync(n => n.UserId == _user.Id);

        Assert.Equal(NotificationType.PAYMENT_RECEIVED, notification.Type);
        Assert.Equal("Ödeme alındı", notification.Title);
        Assert.Contains("42", notification.Body);
        Assert.False(notification.IsRead);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DispatchAsync_AdminFlagAlert_PersistsFlagId_AndPushesFlagTarget()
    {
        var (dispatcher, _, realtimePublisher) = CreateSut();

        var flagId = Guid.NewGuid();
        await dispatcher.DispatchAsync(
            new NotificationRequest
            {
                UserId = _user.Id,
                Type = NotificationType.ADMIN_FLAG_ALERT,
                // TransactionId intentionally omitted — the flag alert links via
                // FlagId (no FK), and a random TransactionId would violate
                // FK_Notifications_Transaction.
                FlagId = flagId,
                Parameters = new Dictionary<string, string>
                {
                    ["TransactionId"] = "(account-level)",
                    ["Reason"] = "PRICE_DEVIATION",
                },
            },
            CancellationToken.None);

        await Context.SaveChangesAsync();

        await using var verify = CreateContext();
        var notification = await verify.Set<Notification>().SingleAsync(n => n.UserId == _user.Id);
        Assert.Equal(NotificationType.ADMIN_FLAG_ALERT, notification.Type);
        // WP8 — FlagId round-trips through the new column.
        Assert.Equal(flagId, notification.FlagId);

        // WP8 — the realtime push resolves the inbox flag target from FlagId.
        var newNotification = realtimePublisher.Calls
            .Where(c => c.Method == "NewNotification")
            .Select(c => (NotificationRealtimePayloads.NewNotification)c.Payload)
            .Single();
        Assert.Equal("flag", newNotification.TargetType);
        Assert.Equal(flagId, newNotification.TargetId);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DispatchAsync_OnlyEnabledChannelsWithExternalId_GetDeliveryRows()
    {
        await SetPreferenceAsync(NotificationChannel.EMAIL, enabled: true, externalId: "user@example.com");
        await SetPreferenceAsync(NotificationChannel.TELEGRAM, enabled: false, externalId: "12345");
        await SetPreferenceAsync(NotificationChannel.DISCORD, enabled: true, externalId: null);

        var (dispatcher, _, _) = CreateSut();
        await dispatcher.DispatchAsync(
            new NotificationRequest
            {
                UserId = _user.Id,
                Type = NotificationType.TRANSACTION_COMPLETED,
                Parameters = new Dictionary<string, string> { ["ItemName"] = "AWP" },
            },
            CancellationToken.None);

        await Context.SaveChangesAsync();

        await using var verify = CreateContext();
        var deliveries = await verify.Set<NotificationDelivery>().ToListAsync();

        Assert.Single(deliveries);
        Assert.Equal(NotificationChannel.EMAIL, deliveries[0].Channel);
        Assert.Equal("user@example.com", deliveries[0].TargetExternalId);
        Assert.Equal(DeliveryStatus.PENDING, deliveries[0].Status);
        Assert.Equal(0, deliveries[0].AttemptCount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DispatchAsync_EnqueuesOneJob_PerDeliveryRow()
    {
        await SetPreferenceAsync(NotificationChannel.EMAIL, enabled: true, externalId: "user@example.com");
        await SetPreferenceAsync(NotificationChannel.TELEGRAM, enabled: true, externalId: "9876");

        var (dispatcher, scheduler, _) = CreateSut();
        await dispatcher.DispatchAsync(
            new NotificationRequest
            {
                UserId = _user.Id,
                Type = NotificationType.PAYMENT_RECEIVED,
                Parameters = new Dictionary<string, string> { ["Amount"] = "10" },
            },
            CancellationToken.None);

        Assert.Equal(2, scheduler.EnqueuedCalls.Count);

        await Context.SaveChangesAsync();
        await using var verify = CreateContext();
        var deliveries = await verify.Set<NotificationDelivery>().ToListAsync();
        Assert.Equal(2, deliveries.Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DispatchAsync_UsesUserPreferredLanguage()
    {
        // Seeded user PreferredLanguage = "tr", so the notification body
        // should render the Turkish template.
        var (dispatcher, _, _) = CreateSut();

        await dispatcher.DispatchAsync(
            new NotificationRequest
            {
                UserId = _user.Id,
                Type = NotificationType.TRANSACTION_INVITE,
                Parameters = new Dictionary<string, string> { ["ItemName"] = "AK-47", ["Amount"] = "5" },
            },
            CancellationToken.None);

        await Context.SaveChangesAsync();

        await using var verify = CreateContext();
        var notification = await verify.Set<Notification>().SingleAsync();

        Assert.Equal("Yeni işlem davetin var", notification.Title);
        Assert.Contains("AK-47 için işlem daveti aldın", notification.Body);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DispatchAsync_FallsBackToEnglishWhenLanguageUnsupportedForKey()
    {
        // Turkish resource omits TRANSACTION_FLAGGED → English neutral entry
        // is used (05 §7.3).
        var (dispatcher, _, _) = CreateSut();

        await dispatcher.DispatchAsync(
            new NotificationRequest
            {
                UserId = _user.Id,
                Type = NotificationType.TRANSACTION_FLAGGED,
                Parameters = new Dictionary<string, string> { ["TransactionId"] = "tx-1" },
            },
            CancellationToken.None);

        await Context.SaveChangesAsync();

        await using var verify = CreateContext();
        var notification = await verify.Set<Notification>().SingleAsync();
        Assert.Equal("Transaction flagged", notification.Title);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DispatchAsync_PushesNewNotificationAndUnreadCount_ViaRealtimePublisher()
    {
        // T62 — every dispatch must result in two /hubs/notifications pushes:
        // a NewNotification carrying the spec §11.2 payload and an
        // UnreadCountChanged with the bumped count (existing unread + 1).
        // Seed one prior unread row so the count assertion is non-trivial.
        Context.Set<Notification>().Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = _user.Id,
            Type = NotificationType.BUYER_ACCEPTED,
            Title = "prior",
            Body = "prior body",
            IsRead = false,
        });
        await Context.SaveChangesAsync();

        var (dispatcher, _, realtimePublisher) = CreateSut();

        await dispatcher.DispatchAsync(
            new NotificationRequest
            {
                UserId = _user.Id,
                Type = NotificationType.PAYMENT_RECEIVED,
                TransactionId = Guid.NewGuid(),
                Parameters = new Dictionary<string, string> { ["Amount"] = "42" },
            },
            CancellationToken.None);

        Assert.Equal(2, realtimePublisher.Calls.Count);

        var newNotification = realtimePublisher.Calls[0];
        Assert.Equal("NewNotification", newNotification.Method);
        Assert.Equal(_user.Id, newNotification.UserId);
        var newPayload = Assert.IsType<NotificationRealtimePayloads.NewNotification>(newNotification.Payload);
        Assert.Equal(NotificationType.PAYMENT_RECEIVED.ToString(), newPayload.Type);
        Assert.Equal("transaction", newPayload.TargetType);
        Assert.NotNull(newPayload.TargetId);

        var unread = realtimePublisher.Calls[1];
        Assert.Equal("UnreadCountChanged", unread.Method);
        Assert.Equal(_user.Id, unread.UserId);
        var unreadPayload = Assert.IsType<NotificationRealtimePayloads.UnreadCountChanged>(unread.Payload);
        Assert.Equal(2, unreadPayload.UnreadCount);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DispatchAsync_DoesNotCallSaveChanges()
    {
        // The dispatcher must leave the unit-of-work boundary to its caller
        // (Outbox dispatcher commits the whole batch). We assert nothing is
        // visible in a fresh context until the test code itself commits.
        var (dispatcher, _, _) = CreateSut();

        await dispatcher.DispatchAsync(
            new NotificationRequest
            {
                UserId = _user.Id,
                Type = NotificationType.PAYMENT_RECEIVED,
                Parameters = new Dictionary<string, string> { ["Amount"] = "1" },
            },
            CancellationToken.None);

        await using var preCommit = CreateContext();
        Assert.Empty(await preCommit.Set<Notification>().ToListAsync());

        await Context.SaveChangesAsync();

        await using var postCommit = CreateContext();
        Assert.Single(await postCommit.Set<Notification>().ToListAsync());
    }
}
