using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.API.Services.HotWallet;
using Skinora.API.Services.Reconciliation;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.API.Tests.Unit.BackgroundJobs;

/// <summary>
/// Unit coverage for the <see cref="ICronJobReconfigurer"/> implementations on
/// the recurring-job registrars (WP14). Verifies that <c>Reconfigure</c>
/// re-registers the correct Hangfire job id with the supplied cron expression
/// through the <see cref="IBackgroundJobScheduler"/> abstraction.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CronJobReconfigurerTests
{
    private sealed class RecordingScheduler : IBackgroundJobScheduler
    {
        public string? LastJobId { get; private set; }
        public string? LastCron { get; private set; }

        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay) => "x";
        public string Enqueue<T>(Expression<Action<T>> methodCall) => "x";
        public bool Delete(string jobId) => true;

        public void AddOrUpdateRecurring<T>(
            string jobId, Expression<Action<T>> methodCall, string cronExpression)
        {
            LastJobId = jobId;
            LastCron = cronExpression;
        }
    }

    private static IServiceScopeFactory ScopeFactoryWith(IBackgroundJobScheduler scheduler)
        => new ServiceCollection()
            .AddSingleton(scheduler)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    [Fact]
    public void ReconciliationRegistrar_Reconfigure_ReRegistersWithNewCron()
    {
        var scheduler = new RecordingScheduler();
        var registrar = new ReconciliationJobRegistrar(
            ScopeFactoryWith(scheduler),
            NullLogger<ReconciliationJobRegistrar>.Instance);

        Assert.Equal("reconciliation.schedule_cron", registrar.CronSettingKey);

        registrar.Reconfigure("0 4 * * *");

        Assert.Equal(ReconciliationJob.RecurringJobId, scheduler.LastJobId);
        Assert.Equal("0 4 * * *", scheduler.LastCron);
    }

    [Fact]
    public void HotWalletRegistrar_Reconfigure_ReRegistersWithNewCron()
    {
        var scheduler = new RecordingScheduler();
        var registrar = new HotWalletMonitorJobRegistrar(
            ScopeFactoryWith(scheduler),
            NullLogger<HotWalletMonitorJobRegistrar>.Instance);

        Assert.Equal("hot_wallet.monitor_cron", registrar.CronSettingKey);

        registrar.Reconfigure("*/30 * * * *");

        Assert.Equal(HotWalletMonitorJob.RecurringJobId, scheduler.LastJobId);
        Assert.Equal("*/30 * * * *", scheduler.LastCron);
    }
}
