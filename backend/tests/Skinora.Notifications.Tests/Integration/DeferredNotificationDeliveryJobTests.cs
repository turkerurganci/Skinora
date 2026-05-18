using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Notifications.Application.Channels;
using Skinora.Notifications.Domain.Entities;
using Skinora.Notifications.Infrastructure.Channels;
using Skinora.Notifications.Infrastructure.DeliveryJobs;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Notifications.Tests.TestSupport;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Notifications.Tests.Integration;

/// <summary>
/// Integration coverage for <see cref="DeferredNotificationDeliveryJob"/>
/// — the T78 deferred-tier (30 dk / 1 sa / 4 sa) follow-up to the
/// immediate-tier delivery pipeline.
/// </summary>
public sealed class DeferredNotificationDeliveryJobTests : IntegrationTestBase
{
    static DeferredNotificationDeliveryJobTests()
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
            SteamId = "76561198000000901",
            SteamDisplayName = "DeferredTester",
            PreferredLanguage = "en",
        };
        context.Set<User>().Add(_user);

        _notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = _user.Id,
            Type = NotificationType.PAYMENT_RECEIVED,
            Title = "Payment received",
            Body = "10 USDT received.",
            IsRead = false,
        };
        context.Set<Notification>().Add(_notification);
        await context.SaveChangesAsync();
    }

    private async Task<NotificationDelivery> SeedDeferredDeliveryAsync(string target = "user@example.com")
    {
        var delivery = new NotificationDelivery
        {
            Id = Guid.NewGuid(),
            NotificationId = _notification.Id,
            Channel = NotificationChannel.EMAIL,
            TargetExternalId = target,
            Status = DeliveryStatus.DEFERRED,
            LastError = "transient failure",
            AttemptCount = NotificationDeliveryJob.MaxRetryAttempts + 1,
        };
        Context.Set<NotificationDelivery>().Add(delivery);
        await Context.SaveChangesAsync();
        return delivery;
    }

    private DeferredNotificationDeliveryJob CreateSut(
        SpyNotificationChannelHandler spy,
        SpyNotificationAdminAlertSink alertSink,
        FakeBackgroundJobScheduler scheduler)
    {
        var handlers = new List<INotificationChannelHandler>
        {
            spy,
            new TelegramNotificationChannelHandler(NullLogger<TelegramNotificationChannelHandler>.Instance),
            new DiscordNotificationChannelHandler(NullLogger<DiscordNotificationChannelHandler>.Instance),
        };

        return new DeferredNotificationDeliveryJob(
            Context,
            handlers,
            alertSink,
            scheduler,
            NullLogger<DeferredNotificationDeliveryJob>.Instance);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_Tier1_Success_FlipsToSent()
    {
        var spy = new SpyNotificationChannelHandler(NotificationChannel.EMAIL);
        var sink = new SpyNotificationAdminAlertSink();
        var scheduler = new FakeBackgroundJobScheduler();
        var delivery = await SeedDeferredDeliveryAsync();

        var sut = CreateSut(spy, sink, scheduler);

        await sut.RunAsync(delivery.Id, tier: 1, CancellationToken.None);

        await using var verify = CreateContext();
        var reloaded = await verify.Set<NotificationDelivery>().SingleAsync(d => d.Id == delivery.Id);

        Assert.Equal(DeliveryStatus.SENT, reloaded.Status);
        Assert.NotNull(reloaded.SentAt);
        Assert.Null(reloaded.LastError);
        Assert.Empty(scheduler.ScheduledCalls);
        Assert.Empty(sink.Alerts);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_Tier1_TransientFailure_StaysDeferredAndSchedulesTier2()
    {
        var spy = new SpyNotificationChannelHandler(NotificationChannel.EMAIL)
        {
            ExceptionFactory = () => new InvalidOperationException("smtp blip"),
        };
        var sink = new SpyNotificationAdminAlertSink();
        var scheduler = new FakeBackgroundJobScheduler();
        var delivery = await SeedDeferredDeliveryAsync();

        var sut = CreateSut(spy, sink, scheduler);

        await sut.RunAsync(delivery.Id, tier: 1, CancellationToken.None);

        await using var verify = CreateContext();
        var reloaded = await verify.Set<NotificationDelivery>().SingleAsync(d => d.Id == delivery.Id);

        Assert.Equal(DeliveryStatus.DEFERRED, reloaded.Status);
        Assert.Contains("smtp blip", reloaded.LastError);
        Assert.Single(scheduler.ScheduledCalls); // tier 2 scheduled
        Assert.Empty(sink.Alerts);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_Tier3_TransientFailure_FlipsToFailedAndAlerts()
    {
        var spy = new SpyNotificationChannelHandler(NotificationChannel.EMAIL)
        {
            ExceptionFactory = () => new InvalidOperationException("still failing"),
        };
        var sink = new SpyNotificationAdminAlertSink();
        var scheduler = new FakeBackgroundJobScheduler();
        var delivery = await SeedDeferredDeliveryAsync();

        var sut = CreateSut(spy, sink, scheduler);

        await sut.RunAsync(delivery.Id, tier: DeferredNotificationDeliveryJob.LastTier, CancellationToken.None);

        await using var verify = CreateContext();
        var reloaded = await verify.Set<NotificationDelivery>().SingleAsync(d => d.Id == delivery.Id);

        Assert.Equal(DeliveryStatus.FAILED, reloaded.Status);
        Assert.Contains("still failing", reloaded.LastError);
        Assert.Empty(scheduler.ScheduledCalls);
        Assert.Single(sink.Alerts);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_PermanentFailure_FlipsToFailedAndAlerts()
    {
        var spy = new SpyNotificationChannelHandler(NotificationChannel.EMAIL)
        {
            ExceptionFactory = () => new PermanentChannelDeliveryException("invalid recipient"),
        };
        var sink = new SpyNotificationAdminAlertSink();
        var scheduler = new FakeBackgroundJobScheduler();
        var delivery = await SeedDeferredDeliveryAsync();

        var sut = CreateSut(spy, sink, scheduler);

        await sut.RunAsync(delivery.Id, tier: 2, CancellationToken.None);

        await using var verify = CreateContext();
        var reloaded = await verify.Set<NotificationDelivery>().SingleAsync(d => d.Id == delivery.Id);

        Assert.Equal(DeliveryStatus.FAILED, reloaded.Status);
        Assert.Contains("invalid recipient", reloaded.LastError);
        Assert.Empty(scheduler.ScheduledCalls);
        Assert.Single(sink.Alerts);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_AlreadySent_NoOps()
    {
        var spy = new SpyNotificationChannelHandler(NotificationChannel.EMAIL);
        var sink = new SpyNotificationAdminAlertSink();
        var scheduler = new FakeBackgroundJobScheduler();
        var delivery = await SeedDeferredDeliveryAsync();
        delivery.Status = DeliveryStatus.SENT;
        delivery.SentAt = DateTime.UtcNow;
        await Context.SaveChangesAsync();

        var sut = CreateSut(spy, sink, scheduler);

        await sut.RunAsync(delivery.Id, tier: 2, CancellationToken.None);

        Assert.Empty(spy.Sends);
        Assert.Empty(scheduler.ScheduledCalls);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RunAsync_InvalidTier_NoOps()
    {
        var spy = new SpyNotificationChannelHandler(NotificationChannel.EMAIL);
        var sink = new SpyNotificationAdminAlertSink();
        var scheduler = new FakeBackgroundJobScheduler();
        var delivery = await SeedDeferredDeliveryAsync();

        var sut = CreateSut(spy, sink, scheduler);

        await sut.RunAsync(delivery.Id, tier: 99, CancellationToken.None);

        // Defensive guard — invalid tier never reaches the handler.
        Assert.Empty(spy.Sends);
        Assert.Empty(scheduler.ScheduledCalls);
    }
}
