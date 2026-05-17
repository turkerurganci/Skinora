using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Persistence;

namespace Skinora.API.Services.HotWallet;

/// <summary>
/// Registers <see cref="HotWalletMonitorJob"/> as a recurring Hangfire job
/// at startup (T77 — 05 §3.3). Same pattern as
/// <c>Skinora.API.Services.Reconciliation.ReconciliationJobRegistrar</c>:
/// the hosted service is a singleton, so the scheduler is resolved through
/// an <see cref="IServiceScopeFactory"/> to keep the DI validator happy.
/// </summary>
public sealed class HotWalletMonitorJobRegistrar : IHostedService
{
    public const string ScheduleCronKey = "hot_wallet.monitor_cron";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HotWalletMonitorJobRegistrar> _logger;

    public HotWalletMonitorJobRegistrar(
        IServiceScopeFactory scopeFactory,
        ILogger<HotWalletMonitorJobRegistrar> logger)
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

            scheduler.AddOrUpdateRecurring<HotWalletMonitorJob>(
                HotWalletMonitorJob.RecurringJobId,
                job => job.Execute(),
                cron);

            _logger.LogInformation(
                "HotWalletMonitorJob registered with cron '{Cron}'.", cron);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "HotWalletMonitorJobRegistrar failed to register the recurring job.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<string> ReadCronAsync(
        AppDbContext db, CancellationToken cancellationToken)
    {
        var raw = await db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == ScheduleCronKey && s.IsConfigured)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw)) return HotWalletMonitorJob.DefaultCron;
        // Hangfire validates the cron syntax at registration; surfacing the
        // raw string keeps the failure visible in the registrar log rather
        // than swallowing the typo with a silent fallback to default.
        return raw.Trim();
    }
}
