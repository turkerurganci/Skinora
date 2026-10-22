using Microsoft.Extensions.Options;
using Prometheus;

namespace Skinora.API.Monitoring;

/// <summary>
/// T164 — publishes <see cref="DatabaseStorageSnapshot"/> for the four
/// database alarms (<c>db-data-size-warning</c> 70 %, <c>db-data-size-critical</c>
/// 85 %, <c>db-backup-full-stale</c>, <c>db-backup-log-stale</c>). Same
/// pattern as <see cref="PrometheusOutboundQueueObserver"/>: the limit and the
/// two age thresholds are published next to the values, the rules divide.
/// Nothing is published before the first reading (a zero there would read as
/// "never backed up").
/// <para>
/// T164 düzeltme turu (S3 F4): the size and backup rules end in
/// <c>or vector(0)</c>, so a series that is never published keeps them silent.
/// <see cref="ProbeSuccess"/> is published on every reading, failed ones
/// included, and <c>db-monitor-probe-failing</c> fires when a part has not
/// been read for 15 minutes — e.g. a backend restarted with no <c>msdb</c>
/// access never publishes a backup timestamp at all.
/// </para>
/// </summary>
public sealed class PrometheusDatabaseStorageObserver
{
    private static readonly GaugeConfiguration Suppressed = new() { SuppressInitialValue = true };

    public static readonly Gauge DataUsedBytes = Metrics.CreateGauge(
        "skinora_backend_db_data_used_bytes",
        "Space used inside the data files of the application database (bytes)",
        Suppressed);

    public static readonly Gauge DataAllocatedBytes = Metrics.CreateGauge(
        "skinora_backend_db_data_allocated_bytes",
        "Allocated size of the data files of the application database (bytes)",
        Suppressed);

    public static readonly Gauge DataLimitBytes = Metrics.CreateGauge(
        "skinora_backend_db_data_limit_bytes",
        "Data-size limit the db-data-size alarms divide by (SQL Server Express: 10240 MB; absent on other editions unless configured)",
        Suppressed);

    public static readonly Gauge LogAllocatedBytes = Metrics.CreateGauge(
        "skinora_backend_db_log_allocated_bytes",
        "Allocated size of the transaction log of the application database (bytes)",
        Suppressed);

    public static readonly Gauge RecoveryModelFull = Metrics.CreateGauge(
        "skinora_backend_db_recovery_model_full",
        "1 when the application database is in FULL recovery (log backups expected), 0 otherwise",
        Suppressed);

    public static readonly Gauge LastBackupTimestamp = Metrics.CreateGauge(
        "skinora_backend_db_last_backup_timestamp_seconds",
        "Unix time the newest scheduled backup of the type finished (msdb); 0 = none recorded",
        new GaugeConfiguration { LabelNames = ["type"], SuppressInitialValue = true });

    public static readonly Gauge BackupAlarmAgeSeconds = Metrics.CreateGauge(
        "skinora_backend_db_backup_alarm_age_seconds",
        "Backup age above which db-backup-<type>-stale fires",
        new GaugeConfiguration { LabelNames = ["type"], SuppressInitialValue = true });

    public static readonly Gauge ProbeSuccess = Metrics.CreateGauge(
        "skinora_backend_db_probe_success",
        "1 when the last reading of the part succeeded, 0 when it failed (part=storage: sizes, edition, recovery model; part=backup_history: msdb)",
        new GaugeConfiguration { LabelNames = ["part"], SuppressInitialValue = true });

    private readonly DatabaseStorageMonitorOptions _options;
    private readonly TimeProvider _clock;

    public PrometheusDatabaseStorageObserver(IOptions<DatabaseStorageMonitorOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    /// <summary>The limit the alarm divides by, in bytes; <see langword="null"/> = none.</summary>
    public static long? ResolveLimitBytes(int engineEdition, long? configuredMegabytes)
    {
        if (configuredMegabytes is > 0) return configuredMegabytes.Value * 1024 * 1024;
        return engineEdition == DatabaseStorageMonitorOptions.ExpressEngineEdition
            ? DatabaseStorageMonitorOptions.ExpressDataLimitMegabytes * 1024 * 1024
            : null;
    }

    /// <summary>A reading that failed as a whole (server unreachable, login refused ...).</summary>
    public void ReportFailure()
    {
        ProbeSuccess.WithLabels("storage").Set(0);
        ProbeSuccess.WithLabels("backup_history").Set(0);
    }

    public void Report(DatabaseStorageSnapshot snapshot)
    {
        ProbeSuccess.WithLabels("storage").Set(1);
        ProbeSuccess.WithLabels("backup_history").Set(snapshot.BackupHistoryRead ? 1 : 0);
        DataUsedBytes.Set(snapshot.DataUsedBytes);
        DataAllocatedBytes.Set(snapshot.DataAllocatedBytes);
        LogAllocatedBytes.Set(snapshot.LogAllocatedBytes);
        if (ResolveLimitBytes(snapshot.EngineEdition, _options.DataLimitMegabytes) is { } limit)
        {
            DataLimitBytes.Set(limit);
        }
        RecoveryModelFull.Set(string.Equals(snapshot.RecoveryModel, "FULL", StringComparison.OrdinalIgnoreCase) ? 1 : 0);

        var fullMaxHours = _options.FullBackupMaxAgeHours > 0
            ? _options.FullBackupMaxAgeHours
            : DatabaseStorageMonitorOptions.DefaultFullBackupMaxAgeHours;
        var logMaxMinutes = _options.LogBackupMaxAgeMinutes > 0
            ? _options.LogBackupMaxAgeMinutes
            : DatabaseStorageMonitorOptions.DefaultLogBackupMaxAgeMinutes;
        BackupAlarmAgeSeconds.WithLabels("full").Set(fullMaxHours * 3600d);
        BackupAlarmAgeSeconds.WithLabels("log").Set(logMaxMinutes * 60d);

        // An unreadable history keeps the last published value (a frozen
        // timestamp still ages into the stale alarm); with none published yet,
        // db-monitor-probe-failing is the alarm (backup_history = 0).
        if (!snapshot.BackupHistoryRead) return;
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        LastBackupTimestamp.WithLabels("full").Set(snapshot.FullBackupAgeSeconds is { } full ? now - full : 0);
        LastBackupTimestamp.WithLabels("log").Set(snapshot.LogBackupAgeSeconds is { } log ? now - log : 0);
    }
}
