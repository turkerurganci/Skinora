using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.Transactions.Application.PaymentMonitoring;

/// <summary>
/// Registers <see cref="EnsurePaymentMonitorJob"/> as a per-minute recurring
/// Hangfire job at startup (T139). Mirrors
/// <c>EnsurePaymentAddressJobRegistrar</c> — scoped scheduler resolved via
/// <see cref="IServiceScopeFactory"/> so the singleton hosted service does not
/// trip ASP.NET Core's DI validator.
/// </summary>
public sealed class EnsurePaymentMonitorJobRegistrar : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EnsurePaymentMonitorJobRegistrar> _logger;

    public EnsurePaymentMonitorJobRegistrar(
        IServiceScopeFactory scopeFactory,
        ILogger<EnsurePaymentMonitorJobRegistrar> logger)
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

            scheduler.AddOrUpdateRecurring<EnsurePaymentMonitorJob>(
                EnsurePaymentMonitorJob.RecurringJobId,
                job => job.Execute(),
                EnsurePaymentMonitorJob.Cron);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "EnsurePaymentMonitorJobRegistrar failed to register the recurring job.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
