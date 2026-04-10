using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Outbox;

namespace Skinora.API.Outbox;

/// <summary>
/// Primes the outbox dispatcher chain when the application starts up
/// (09 §13.4 — "Uygulama başlangıcında bir kez tetikle").
/// </summary>
/// <remarks>
/// <para>
/// The first invocation enqueues a one-shot Hangfire job that calls
/// <see cref="IOutboxDispatcher.ProcessAndRescheduleAsync"/>; from then on
/// the dispatcher's <c>try/finally</c> reschedules itself with the
/// configured polling interval. Distributed lock guarantees that several
/// instances starting up simultaneously do not multiply the chain.
/// </para>
/// <para>
/// Implemented as <see cref="IHostedService"/> rather than inline in
/// <c>Program.cs</c> so it is testable in isolation and so test factories
/// can opt-out by removing the hosted service descriptor.
/// </para>
/// </remarks>
public class OutboxStartupHook : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxStartupHook> _logger;

    public OutboxStartupHook(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxStartupHook> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var jobScheduler = scope.ServiceProvider.GetRequiredService<IBackgroundJobScheduler>();

            var jobId = jobScheduler.Enqueue<IOutboxDispatcher>(
                d => d.ProcessAndRescheduleAsync());

            _logger.LogInformation(
                "Outbox dispatcher chain primed (Hangfire job {JobId}).",
                jobId);
        }
        catch (Exception ex)
        {
            // Do not block application startup if the scheduler is
            // unreachable — the next process restart re-runs StartAsync.
            _logger.LogError(ex, "Outbox dispatcher startup priming failed.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
