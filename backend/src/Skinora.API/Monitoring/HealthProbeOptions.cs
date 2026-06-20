namespace Skinora.API.Monitoring;

/// <summary>
/// Operational tuning for the WP16 platform health probe (05 §4.4, 02 §3.3).
/// Bound from the <c>HealthProbe</c> configuration section. These are
/// infrastructure knobs (poll cadence, failure tolerance) rather than business
/// parameters, so they live in <c>appsettings.json</c> alongside
/// <c>HangfireOptions</c> / <c>Timeouts</c> instead of in <c>SystemSettings</c>.
/// </summary>
public sealed class HealthProbeOptions
{
    public const string SectionName = "HealthProbe";

    /// <summary>
    /// When false the probe recurring job is never registered. Lets an
    /// environment without reachable sidecars (local dev) silence the alert
    /// without code changes. Default true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cron expression for the probe cadence (UTC). Default every minute —
    /// combined with <see cref="FailureThreshold"/> this gives a detection
    /// latency of roughly <c>FailureThreshold</c> minutes.
    /// </summary>
    public string ProbeCron { get; set; } = "* * * * *";

    /// <summary>
    /// Consecutive failed probes before a component is declared DEGRADED and the
    /// admin alert fires. Tolerates transient blips. Default 3.
    /// </summary>
    public int FailureThreshold { get; set; } = 3;
}
