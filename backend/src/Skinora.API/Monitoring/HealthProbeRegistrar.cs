using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.API.Monitoring;

/// <summary>
/// Registers the WP16 platform health probe as a Hangfire recurring job at
/// startup (05 §4.4, 02 §3.3). Mirrors <c>RetentionJobsRegistrar</c>: opens its
/// own scope because <see cref="IBackgroundJobScheduler"/> is scoped, and treats
/// a scheduler outage at startup as non-fatal (the next restart retries).
/// </summary>
public sealed class HealthProbeRegistrar : IHostedService
{
    /// <summary>Stable recurring-job id so re-registration updates in place.</summary>
    public const string RecurringJobId = "platform-health-probe";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HealthProbeOptions _options;
    private readonly ILogger<HealthProbeRegistrar> _logger;

    public HealthProbeRegistrar(
        IServiceScopeFactory scopeFactory,
        IOptions<HealthProbeOptions> options,
        ILogger<HealthProbeRegistrar> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Platform health probe disabled (HealthProbe:Enabled=false) — recurring job not registered.");
            return Task.CompletedTask;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundJobScheduler>();

            scheduler.AddOrUpdateRecurring<IPlatformHealthProbeJob>(
                RecurringJobId,
                job => job.ProbeAsync(),
                _options.ProbeCron);
        }
        catch (Exception ex)
        {
            // Monitoring is a maintenance concern, not a correctness one — an
            // unreachable scheduler at start-up does not block the host.
            _logger.LogWarning(ex,
                "HealthProbeRegistrar failed to register the platform health probe recurring job.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
