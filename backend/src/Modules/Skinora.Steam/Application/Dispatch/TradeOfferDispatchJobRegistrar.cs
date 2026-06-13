using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.Steam.Application.Dispatch;

/// <summary>
/// Registers the <see cref="TradeOfferDispatchJob"/> per-minute recurring job at
/// startup (T106a). Mirrors <c>OutgoingTransferJobsRegistrar</c>'s
/// <see cref="IServiceScopeFactory"/> pattern so the singleton hosted service
/// does not capture scoped dependencies.
/// </summary>
public sealed class TradeOfferDispatchJobRegistrar : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TradeOfferDispatchJobRegistrar> _logger;

    public TradeOfferDispatchJobRegistrar(
        IServiceScopeFactory scopeFactory,
        ILogger<TradeOfferDispatchJobRegistrar> logger)
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

            scheduler.AddOrUpdateRecurring<TradeOfferDispatchJob>(
                TradeOfferDispatchJob.RecurringJobId,
                job => job.Execute(),
                TradeOfferDispatchJob.Cron);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "TradeOfferDispatchJobRegistrar failed to register the recurring job.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
