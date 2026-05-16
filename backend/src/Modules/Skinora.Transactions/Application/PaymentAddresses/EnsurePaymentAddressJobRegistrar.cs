using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.Transactions.Application.PaymentAddresses;

/// <summary>
/// Registers <see cref="EnsurePaymentAddressJob"/> as a per-minute recurring
/// Hangfire job at startup. Mirrors
/// <c>RefreshTokenCleanupJobRegistrar</c> — scoped scheduler resolved via
/// <see cref="IServiceScopeFactory"/> so the singleton hosted service does
/// not trip ASP.NET Core's DI validator.
/// </summary>
public sealed class EnsurePaymentAddressJobRegistrar : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EnsurePaymentAddressJobRegistrar> _logger;

    public EnsurePaymentAddressJobRegistrar(
        IServiceScopeFactory scopeFactory,
        ILogger<EnsurePaymentAddressJobRegistrar> logger)
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

            scheduler.AddOrUpdateRecurring<EnsurePaymentAddressJob>(
                EnsurePaymentAddressJob.RecurringJobId,
                job => job.Execute(),
                EnsurePaymentAddressJob.Cron);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "EnsurePaymentAddressJobRegistrar failed to register the recurring job.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
