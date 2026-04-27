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

    private (NotificationDispatcher Dispatcher, FakeBackgroundJobScheduler Scheduler) CreateSut()
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

        var dispatcher = new NotificationDispatcher(
            Context,
            resolver,
            scheduler,
            NullLogger<NotificationDispatcher>.Instance);

        return (dispatcher, scheduler);
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
        var (dispatcher, _) = CreateSut();

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
    public async Task DispatchAsync_OnlyEnabledChannelsWithExternalId_GetDeliveryRows()
    {
        await SetPreferenceAsync(NotificationChannel.EMAIL, enabled: true, externalId: "user@example.com");
        await SetPreferenceAsync(NotificationChannel.TELEGRAM, enabled: false, externalId: "12345");
        await SetPreferenceAsync(NotificationChannel.DISCORD, enabled: true, externalId: null);

        var (dispatcher, _) = CreateSut();
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

        var (dispatcher, scheduler) = CreateSut();
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
        var (dispatcher, _) = CreateSut();

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
        var (dispatcher, _) = CreateSut();

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
    public async Task DispatchAsync_DoesNotCallSaveChanges()
    {
        // The dispatcher must leave the unit-of-work boundary to its caller
        // (Outbox dispatcher commits the whole batch). We assert nothing is
        // visible in a fresh context until the test code itself commits.
        var (dispatcher, _) = CreateSut();

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
