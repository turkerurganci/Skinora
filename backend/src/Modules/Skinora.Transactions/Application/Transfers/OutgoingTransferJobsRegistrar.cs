using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// Registers the two outbound-transfer Hangfire recurring jobs at startup
/// (T73). Mirrors <c>EnsurePaymentAddressJobRegistrar</c>'s
/// <see cref="IServiceScopeFactory"/> pattern so the singleton hosted service
/// does not capture scoped dependencies.
/// </summary>
public sealed class OutgoingTransferJobsRegistrar : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutgoingTransferJobsRegistrar> _logger;

    public OutgoingTransferJobsRegistrar(
        IServiceScopeFactory scopeFactory,
        ILogger<OutgoingTransferJobsRegistrar> logger)
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

            scheduler.AddOrUpdateRecurring<OutgoingTransferDispatchJob>(
                OutgoingTransferDispatchJob.RecurringJobId,
                job => job.Execute(),
                OutgoingTransferDispatchJob.Cron);

            scheduler.AddOrUpdateRecurring<OutgoingTransferConfirmationJob>(
                OutgoingTransferConfirmationJob.RecurringJobId,
                job => job.Execute(),
                OutgoingTransferConfirmationJob.Cron);

            // WP1 — produce the SELLER_PAYOUT row once a transaction reaches
            // ITEM_DELIVERED so the dispatch + confirmation jobs above can
            // settle it and drive the transaction to COMPLETED.
            scheduler.AddOrUpdateRecurring<SellerPayoutQueueJob>(
                SellerPayoutQueueJob.RecurringJobId,
                job => job.Execute(),
                SellerPayoutQueueJob.Cron);

            // WP3 — produce the deposit → hot wallet SWEEP row at the same
            // ITEM_DELIVERED gate (deferred past the buyer-refund window). The
            // dispatch + confirmation jobs above broadcast it to the sidecar's
            // /api/transfer/sweep endpoint and drive it to CONFIRMED so the
            // daily reconciliation (T76) is two-sided for the hot wallet.
            scheduler.AddOrUpdateRecurring<SweepQueueJob>(
                SweepQueueJob.RecurringJobId,
                job => job.Execute(),
                SweepQueueJob.Cron);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "OutgoingTransferJobsRegistrar failed to register recurring jobs.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
