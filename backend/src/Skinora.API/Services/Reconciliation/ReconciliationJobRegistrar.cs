using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Persistence;

namespace Skinora.API.Services.Reconciliation;

/// <summary>
/// Registers <see cref="ReconciliationJob"/> as a daily Hangfire recurring
/// job at startup (T76 — 05 §3.3). Mirrors the
/// <see cref="EnsurePaymentAddressJobRegistrar"/> pattern: the hosted
/// service is a singleton, so the scheduler is resolved through an
/// <see cref="IServiceScopeFactory"/> to keep the DI validator happy.
///
/// <para>
/// The cron expression is read from the
/// <c>reconciliation.schedule_cron</c> SystemSetting if present and
/// <c>IsConfigured = true</c>; otherwise the documented default
/// (<see cref="ReconciliationJob.DefaultCron"/>) ships. The setting is
/// read once at startup; runtime cadence overrides require a host restart
/// (forward-deferred to T96 admin tooling).
/// </para>
/// </summary>
public sealed class ReconciliationJobRegistrar : IHostedService, ICronJobReconfigurer
{
    public const string ScheduleCronKey = "reconciliation.schedule_cron";

    /// <inheritdoc />
    public string CronSettingKey => ScheduleCronKey;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReconciliationJobRegistrar> _logger;

    public ReconciliationJobRegistrar(
        IServiceScopeFactory scopeFactory,
        ILogger<ReconciliationJobRegistrar> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundJobScheduler>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var cron = await ReadCronAsync(db, cancellationToken);

            RegisterRecurring(scheduler, cron);

            _logger.LogInformation(
                "ReconciliationJob registered with cron '{Cron}'.", cron);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ReconciliationJobRegistrar failed to register the recurring job.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void Reconfigure(string cronExpression)
    {
        using var scope = _scopeFactory.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundJobScheduler>();
        RegisterRecurring(scheduler, cronExpression);
    }

    private static void RegisterRecurring(IBackgroundJobScheduler scheduler, string cron) =>
        scheduler.AddOrUpdateRecurring<ReconciliationJob>(
            ReconciliationJob.RecurringJobId,
            job => job.Execute(),
            cron);

    private static async Task<string> ReadCronAsync(
        AppDbContext db, CancellationToken cancellationToken)
    {
        var raw = await db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == ScheduleCronKey && s.IsConfigured)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw)) return ReconciliationJob.DefaultCron;
        var trimmed = raw.Trim();
        // Hangfire validates the cron syntax at registration; surfacing the
        // raw string keeps the failure visible in the registrar log rather
        // than swallowing the typo with a silent fallback to default.
        return trimmed;
    }
}
