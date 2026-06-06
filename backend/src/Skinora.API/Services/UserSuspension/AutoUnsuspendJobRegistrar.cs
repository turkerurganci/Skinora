using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.API.Services.UserSuspension;

/// <summary>
/// Registers <see cref="AutoUnsuspendJob"/> as a recurring Hangfire job at
/// startup (every 6 hours). Mirrors the <c>RefreshTokenCleanupJobRegistrar</c>
/// scope-per-start pattern — <see cref="IBackgroundJobScheduler"/> is scoped,
/// so a singleton hosted service must open a scope to resolve it.
/// </summary>
public sealed class AutoUnsuspendJobRegistrar : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoUnsuspendJobRegistrar> _logger;

    public AutoUnsuspendJobRegistrar(
        IServiceScopeFactory scopeFactory,
        ILogger<AutoUnsuspendJobRegistrar> logger)
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

            scheduler.AddOrUpdateRecurring<AutoUnsuspendJob>(
                AutoUnsuspendJob.RecurringJobId,
                job => job.Execute(),
                AutoUnsuspendJob.Cron);
        }
        catch (Exception ex)
        {
            // Don't block startup — temp-block expiry is a maintenance concern,
            // not a correctness one (a missed sweep just delays auto-unsuspend
            // until the next run; admins can still unsuspend manually).
            _logger.LogWarning(ex,
                "AutoUnsuspendJobRegistrar failed to register the recurring job.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
