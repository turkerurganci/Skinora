using Skinora.Platform.Application.Settings;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.API.BackgroundJobs;

/// <summary>
/// API-host implementation of <see cref="ISettingChangePropagator"/> (WP14).
/// When an admin updates a cron-scheduled SystemSetting
/// (<c>reconciliation.schedule_cron</c>, <c>hot_wallet.monitor_cron</c>), the
/// matching <see cref="ICronJobReconfigurer"/> re-registers its Hangfire
/// recurring job so the new schedule takes effect immediately instead of at
/// the next host restart.
/// </summary>
public sealed class CronSettingChangePropagator : ISettingChangePropagator
{
    private readonly IEnumerable<ICronJobReconfigurer> _reconfigurers;
    private readonly ILogger<CronSettingChangePropagator> _logger;

    public CronSettingChangePropagator(
        IEnumerable<ICronJobReconfigurer> reconfigurers,
        ILogger<CronSettingChangePropagator> logger)
    {
        _reconfigurers = reconfigurers;
        _logger = logger;
    }

    public Task PropagateAsync(string key, string value, CancellationToken cancellationToken)
    {
        var target = _reconfigurers.FirstOrDefault(r =>
            string.Equals(r.CronSettingKey, key, StringComparison.Ordinal));

        // Most setting changes are not cron-scheduled keys — nothing to do.
        if (target is null)
            return Task.CompletedTask;

        try
        {
            target.Reconfigure(value);
            _logger.LogInformation(
                "Cron job for setting '{Key}' re-registered with '{Cron}' after an admin change.",
                key, value);
        }
        catch (Exception ex)
        {
            // Best-effort: the authoritative DB write already committed. A
            // failure here (e.g. a transient Hangfire storage error) leaves the
            // recurring job on its previous schedule until the next host restart
            // re-reads the setting. Surface it in the logs rather than failing
            // the admin request, which already succeeded.
            _logger.LogWarning(ex,
                "Failed to re-register the cron job for setting '{Key}'; it keeps its previous schedule until restart.",
                key);
        }

        return Task.CompletedTask;
    }
}
