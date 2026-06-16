using Skinora.Shared.BackgroundJobs;

namespace Skinora.API.Services.Fraud;

/// <summary>
/// Registers <see cref="MultiAccountRetroScanJob"/> as a daily recurring
/// Hangfire job at startup (WP4b). Mirrors the <c>AutoUnsuspendJobRegistrar</c>
/// scope-per-start pattern — <see cref="IBackgroundJobScheduler"/> is scoped, so
/// a singleton hosted service must open a scope to resolve it.
/// </summary>
public sealed class MultiAccountRetroScanJobRegistrar : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MultiAccountRetroScanJobRegistrar> _logger;

    public MultiAccountRetroScanJobRegistrar(
        IServiceScopeFactory scopeFactory,
        ILogger<MultiAccountRetroScanJobRegistrar> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundJobScheduler>();

            scheduler.AddOrUpdateRecurring<MultiAccountRetroScanJob>(
                MultiAccountRetroScanJob.RecurringJobId,
                job => job.Execute(),
                MultiAccountRetroScanJob.Cron);
        }
        catch (Exception ex)
        {
            // Don't block startup — a missed retro-scan only delays catching a
            // multi-account link until the next run (the wallet-update event
            // hook still flags new collisions in real time).
            _logger.LogWarning(ex,
                "MultiAccountRetroScanJobRegistrar failed to register the recurring job.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
