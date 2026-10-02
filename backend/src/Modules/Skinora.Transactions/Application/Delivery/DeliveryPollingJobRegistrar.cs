using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// Registers <see cref="DeliveryPollingJob"/> as a recurring Hangfire job on
/// startup (P2P-DeliveryPollingJob). Same shape as the payment-monitor
/// reconciler's registrar: a registration failure is logged, never fatal — the
/// deadline round still covers every delivery, only later.
/// </summary>
public sealed class DeliveryPollingJobRegistrar : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeliveryPollingJobRegistrar> _logger;

    public DeliveryPollingJobRegistrar(
        IServiceScopeFactory scopeFactory,
        ILogger<DeliveryPollingJobRegistrar> logger)
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

            scheduler.AddOrUpdateRecurring<DeliveryPollingJob>(
                DeliveryPollingJob.RecurringJobId,
                job => job.Execute(),
                DeliveryPollingJob.Cron);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DeliveryPollingJobRegistrar failed to register the recurring job.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
