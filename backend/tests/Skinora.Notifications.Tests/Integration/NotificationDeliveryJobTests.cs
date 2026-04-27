using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Application.Templates;
using Skinora.Notifications.Domain.Entities;
using Skinora.Notifications.Infrastructure.Channels;
using Skinora.Notifications.Infrastructure.DeliveryJobs;
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
/// Integration coverage for <see cref="NotificationDeliveryJob"/> — verifies
/// retry semantics (05 §7.5), idempotency (already-SENT short circuit),
/// missing-row defensive no-op (09 §13.3) and admin alert on the final
/// attempt.
/// </summary>
public class NotificationDeliveryJobTests : IntegrationTestBase
{
    static NotificationDeliveryJobTests()
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
            SteamId = "76561198000000301",
            SteamDisplayName = "DeliveryTester",
            PreferredLanguage = "en",
        };
        context.Set<User>().Add(_user);

        _notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = _user.Id,
            Type = NotificationType.PAYMENT_RECEIVED,
            Title = "Payment received",
            Body = "We received 10 USDT for your transaction.",
            IsRead = false,
        };
        context.Set<Notification>().Add(_notification);

        await context.SaveChangesAsync();
    }

    private NotificationDeliveryJob CreateSut(
        SpyNotificationChannelHandler? spy = null,
        SpyNotificationAdminAlertSink? alertSink = null)
    {
        var handlers = new List<INotificationChannelHandler>
        {
            spy ?? new SpyNotificationChannelHandler(NotificationChannel.EMAIL),
            new TelegramNotificationChannelHandler(NullLogger<TelegramNotificationChannelHandler>.Instance),
            new DiscordNotificationChannelHandler(NullLogger<DiscordNotificationChannelHandler>.Instance),
        };

        var services = new ServiceCollection();
        services.AddLocalization();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var localizer = provider.GetRequiredService<IStringLocalizer<NotificationTemplates>>();
        var resolver = new ResxNotificationTemplateResolver(
            localizer,
            NullLogger<ResxNotificationTemplateResolver>.Instance);

        return new NotificationDeliveryJob(
            Context,
            handlers,
            resolver,
            alertSink ?? new SpyNotificationAdminAlertSink(),
            NullLogger<NotificationDeliveryJob>.Instance);
    }

    private async Task<NotificationDelivery> AddDeliveryAsync(NotificationChannel channel, string target)
    {
        var delivery = new NotificationDelivery
        {
            Id = Guid.NewGuid(),
            NotificationId = _notification.Id,
            Channel = channel,
            TargetExternalId = target,
            Status = DeliveryStatus.PENDING,
            AttemptCount = 0,
        };
        Context.Set<NotificationDelivery>().Add(delivery);
        await Context.SaveChangesAsync();
        return delivery;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_DeliveryMissing_NoOps()
    {
        var sut = CreateSut();

        // Random unknown id → producer transaction never committed (09 §13.3).
        await sut.RunAsync(Guid.NewGuid(), attemptNumber: 1, CancellationToken.None);

        // Should not have thrown; nothing else to assert.
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_AlreadySent_ShortCircuits()
    {
        var spy = new SpyNotificationChannelHandler(NotificationChannel.EMAIL);
        var delivery = await AddDeliveryAsync(NotificationChannel.EMAIL, "user@example.com");
        delivery.Status = DeliveryStatus.SENT;
        delivery.SentAt = DateTime.UtcNow;
        await Context.SaveChangesAsync();

        var sut = CreateSut(spy);
        await sut.RunAsync(delivery.Id, attemptNumber: 1, CancellationToken.None);

        Assert.Empty(spy.Sends);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_Success_MarksSentAndIncrementsAttempt()
    {
        var spy = new SpyNotificationChannelHandler(NotificationChannel.EMAIL);
        var delivery = await AddDeliveryAsync(NotificationChannel.EMAIL, "user@example.com");

        var sut = CreateSut(spy);
        await sut.RunAsync(delivery.Id, attemptNumber: 1, CancellationToken.None);

        Assert.Single(spy.Sends);
        Assert.Equal("user@example.com", spy.Sends[0].Target);

        await using var verify = CreateContext();
        var reloaded = await verify.Set<NotificationDelivery>().SingleAsync(d => d.Id == delivery.Id);
        Assert.Equal(DeliveryStatus.SENT, reloaded.Status);
        Assert.Equal(1, reloaded.AttemptCount);
        Assert.NotNull(reloaded.SentAt);
        Assert.Null(reloaded.LastError);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_TransientFailure_MarksFailedAndThrowsForRetry()
    {
        var spy = new SpyNotificationChannelHandler(NotificationChannel.EMAIL)
        {
            ExceptionFactory = () => new InvalidOperationException("smtp transient"),
        };
        var alertSink = new SpyNotificationAdminAlertSink();
        var delivery = await AddDeliveryAsync(NotificationChannel.EMAIL, "user@example.com");

        var sut = CreateSut(spy, alertSink);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.RunAsync(delivery.Id, attemptNumber: 1, CancellationToken.None));

        await using var verify = CreateContext();
        var reloaded = await verify.Set<NotificationDelivery>().SingleAsync(d => d.Id == delivery.Id);
        Assert.Equal(DeliveryStatus.FAILED, reloaded.Status);
        Assert.Equal(1, reloaded.AttemptCount);
        Assert.Contains("smtp transient", reloaded.LastError);
        Assert.Empty(alertSink.Alerts);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_FinalAttemptFailure_RaisesAdminAlertAndThrows()
    {
        var spy = new SpyNotificationChannelHandler(NotificationChannel.EMAIL)
        {
            ExceptionFactory = () => new InvalidOperationException("permanent"),
        };
        var alertSink = new SpyNotificationAdminAlertSink();
        var delivery = await AddDeliveryAsync(NotificationChannel.EMAIL, "user@example.com");

        var sut = CreateSut(spy, alertSink);

        // Attempt MaxRetryAttempts + 1 = the run after the final retry —
        // the 1m/5m/15m budget is exhausted (05 §7.5).
        var finalAttempt = NotificationDeliveryJob.MaxRetryAttempts + 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.RunAsync(delivery.Id, finalAttempt, CancellationToken.None));

        Assert.Single(alertSink.Alerts);
        Assert.Equal(delivery.Id, alertSink.Alerts[0].Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_ChannelSelectionMatchesDeliveryChannel()
    {
        var emailSpy = new SpyNotificationChannelHandler(NotificationChannel.EMAIL);
        var telegramSpy = new SpyNotificationChannelHandler(NotificationChannel.TELEGRAM);
        var alertSink = new SpyNotificationAdminAlertSink();

        var handlers = new List<INotificationChannelHandler> { emailSpy, telegramSpy };
        var services = new ServiceCollection();
        services.AddLocalization();
        services.AddLogging();
        var provider = services.BuildServiceProvider();
        var localizer = provider.GetRequiredService<IStringLocalizer<NotificationTemplates>>();
        var resolver = new ResxNotificationTemplateResolver(
            localizer,
            NullLogger<ResxNotificationTemplateResolver>.Instance);

        var sut = new NotificationDeliveryJob(
            Context,
            handlers,
            resolver,
            alertSink,
            NullLogger<NotificationDeliveryJob>.Instance);

        var telegramDelivery = await AddDeliveryAsync(NotificationChannel.TELEGRAM, "12345");

        await sut.RunAsync(telegramDelivery.Id, attemptNumber: 1, CancellationToken.None);

        Assert.Empty(emailSpy.Sends);
        Assert.Single(telegramSpy.Sends);
        Assert.Equal("12345", telegramSpy.Sends[0].Target);
    }
}
