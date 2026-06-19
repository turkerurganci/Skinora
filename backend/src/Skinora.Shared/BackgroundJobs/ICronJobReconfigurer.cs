namespace Skinora.Shared.BackgroundJobs;

/// <summary>
/// Implemented by the hosted-service registrar of a cron-scheduled Hangfire
/// recurring job whose schedule is sourced from a SystemSetting
/// (<c>reconciliation.schedule_cron</c>, <c>hot_wallet.monitor_cron</c> — WP14).
/// </summary>
/// <remarks>
/// <para>
/// Before WP14 these crons were read <i>once</i> at <c>StartAsync</c>, so an
/// admin cadence change only took effect after a host restart (the deferral
/// noted in the seed comments as "admin runtime override T96 devir"). This
/// abstraction lets the settings-change propagator re-register the recurring
/// job at runtime <b>without</b> the Platform module taking a direct reference
/// to the API-host job types (which live in <c>Skinora.API</c> and cannot be
/// referenced from a module assembly).
/// </para>
/// </remarks>
public interface ICronJobReconfigurer
{
    /// <summary>The SystemSetting key that supplies this job's cron expression.</summary>
    string CronSettingKey { get; }

    /// <summary>
    /// Re-register the recurring job with <paramref name="cronExpression"/> so
    /// the new schedule takes effect immediately. The expression is assumed
    /// pre-validated by <c>SystemSettingsValidator</c>; the underlying
    /// scheduler re-validates and throws on a malformed expression.
    /// </summary>
    void Reconfigure(string cronExpression);
}
