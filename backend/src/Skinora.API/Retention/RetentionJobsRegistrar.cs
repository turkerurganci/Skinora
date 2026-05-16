using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.API.Retention;

/// <summary>
/// Registers all T63b retention recurring jobs at startup. One hosted service
/// covers the three jobs because they share a cron family (off-peak, daily or
/// weekly) and a single failure path makes scheduler outages easier to
/// diagnose than three sibling registrars logging the same exception.
/// </summary>
/// <remarks>
/// Mirrors the <see cref="Skinora.Auth.Application.Session.RefreshTokenCleanupJobRegistrar"/>
/// pattern: <see cref="IBackgroundJobScheduler"/> is scoped (Hangfire's client
/// is scoped) so the registrar opens its own scope at start-up. ASP.NET Core's
/// DI validator would otherwise reject a scoped service injected into a
/// singleton hosted service.
/// </remarks>
public sealed class RetentionJobsRegistrar : IHostedService
{
    /// <summary>03:30 UTC daily — outbox retention sweep.</summary>
    public const string OutboxCron = "30 3 * * *";

    /// <summary>04:00 UTC every Sunday — orphan notification sweep (weekly because the window is a year).</summary>
    public const string OrphanNotificationCron = "0 4 * * 0";

    /// <summary>04:30 UTC every Sunday — user login log sweep (weekly, same reason).</summary>
    public const string UserLoginLogCron = "30 4 * * 0";

    /// <summary>Every 15 minutes — expired webhook nonces (T68, retention ≥ replay window).</summary>
    public const string ProcessedNonceCron = "*/15 * * * *";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RetentionJobsRegistrar> _logger;

    public RetentionJobsRegistrar(
        IServiceScopeFactory scopeFactory,
        ILogger<RetentionJobsRegistrar> logger)
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

            scheduler.AddOrUpdateRecurring<OutboxRetentionCleanupJob>(
                OutboxRetentionCleanupJob.RecurringJobId,
                job => job.Execute(),
                OutboxCron);

            scheduler.AddOrUpdateRecurring<OrphanNotificationRetentionCleanupJob>(
                OrphanNotificationRetentionCleanupJob.RecurringJobId,
                job => job.Execute(),
                OrphanNotificationCron);

            scheduler.AddOrUpdateRecurring<UserLoginLogRetentionCleanupJob>(
                UserLoginLogRetentionCleanupJob.RecurringJobId,
                job => job.Execute(),
                UserLoginLogCron);

            scheduler.AddOrUpdateRecurring<ProcessedNonceCleanupJob>(
                ProcessedNonceCleanupJob.RecurringJobId,
                job => job.Execute(),
                ProcessedNonceCron);
        }
        catch (Exception ex)
        {
            // Retention is a maintenance concern, not a correctness one — an
            // unreachable scheduler at start-up does not block the host. The
            // next process restart retries StartAsync.
            _logger.LogWarning(ex,
                "RetentionJobsRegistrar failed to register one or more recurring jobs.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
