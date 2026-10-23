using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skinora.API.Monitoring;
using Skinora.API.Services.Reconciliation;
using Skinora.API.Tests.Common;
using Skinora.Notifications.Application.EventHandlers;
using Skinora.Shared.Events;
using Skinora.Shared.Notifications;
using Skinora.Shared.Persistence;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T165 düzeltme turu (doğrulama bulgu 4, ⚠ PARA — bozma M24) — the API host's
/// composition of the money-alarm paths. Removing the host's
/// <see cref="PrometheusAdminMoneyAlarmObserver"/> registration silently fell
/// back to the module's Null observer and <c>admin-money-alarm-raised</c> died
/// with every test green. These resolve what the real host builds.
/// </summary>
public class MoneyAlarmCompositionTests : IClassFixture<HangfireBypassFactory>
{
    private readonly IServiceProvider _services;

    public MoneyAlarmCompositionTests(HangfireBypassFactory factory)
    {
        _services = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<AppDbContext>(options => options.UseSqlite("DataSource=:memory:"));
            });
        }).Services;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Host_PublishesBackendMoneyAlarms_ToPrometheus()
    {
        Assert.IsType<PrometheusAdminMoneyAlarmObserver>(_services.GetRequiredService<IAdminMoneyAlarmObserver>());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Host_PublishesReconciliationRounds_ToPrometheus()
    {
        Assert.IsType<PrometheusReconciliationRunObserver>(_services.GetRequiredService<IReconciliationRunObserver>());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Host_MoneyConsumers_GetThePrometheusObserver()
    {
        using var scope = _services.CreateScope();
        var refundBlocked = Assert.Single(
            scope.ServiceProvider.GetServices<INotificationHandler<RefundBlockedAdminAlertEvent>>()
                .OfType<RefundBlockedAdminNotificationConsumer>());
        Assert.True(refundBlocked.IsMoneyAlarm);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Host_ReconciliationMismatch_ReachesTheAdminConsumer()
    {
        using var scope = _services.CreateScope();
        Assert.Single(
            scope.ServiceProvider.GetServices<INotificationHandler<ReconciliationMismatchAdminAlertEvent>>()
                .OfType<ReconciliationMismatchAdminNotificationConsumer>());
    }
}
