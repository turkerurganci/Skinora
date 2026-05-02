using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Application.Heartbeat;
using Skinora.Shared.BackgroundJobs;
using Skinora.Transactions.Application.Timeouts;

namespace Skinora.API.BackgroundJobs.Timeouts;

/// <summary>
/// Primes the T47 timeout scheduling chains at application startup
/// (05 §4.4 + 09 §13.4):
/// (1) runs <see cref="IRestartRecoveryService"/> once,
/// (2) primes the heartbeat self-rescheduling chain,
/// (3) primes the deadline-scanner self-rescheduling chain.
/// Mirrors <c>OutboxStartupHook</c> so failures during scheduler bring-up are
/// logged but do not block the API host from starting.
/// </summary>
public sealed class TimeoutSchedulerStartupHook : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TimeoutSchedulerStartupHook> _logger;

    public TimeoutSchedulerStartupHook(
        IServiceScopeFactory scopeFactory,
        ILogger<TimeoutSchedulerStartupHook> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();

            // Step 1 — restart recovery (outage-window deadline extension).
            var recovery = scope.ServiceProvider.GetRequiredService<IRestartRecoveryService>();
            var result = await recovery.RunAsync(cancellationToken);
            if (result.ExtensionApplied)
            {
                _logger.LogInformation(
                    "Restart recovery applied: outage={OutageSeconds}s, extended={Extended}, paymentRescheduled={Resched}.",
                    (long)result.OutageWindow.TotalSeconds,
                    result.ExtendedTransactionCount,
                    result.RescheduledPaymentJobCount);
            }

            // Step 2 — heartbeat self-rescheduling chain.
            var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundJobScheduler>();
            var heartbeatJobId = scheduler.Enqueue<IHeartbeatJob>(j => j.TickAsync());
            _logger.LogInformation("Heartbeat chain primed (Hangfire job {JobId}).", heartbeatJobId);

            // Step 3 — deadline scanner self-rescheduling chain.
            var scannerJobId = scheduler.Enqueue<IDeadlineScannerJob>(j => j.ScanAndRescheduleAsync());
            _logger.LogInformation("Deadline scanner chain primed (Hangfire job {JobId}).", scannerJobId);
        }
        catch (Exception ex)
        {
            // Do not block startup — the next process restart re-runs StartAsync.
            _logger.LogError(ex, "Timeout scheduler startup priming failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
