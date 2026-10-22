namespace Skinora.API.Monitoring;

/// <summary>
/// T164 — the database storage / backup monitor (section
/// <c>DatabaseStorageMonitor</c>, env <c>DatabaseStorageMonitor__*</c>).
/// Deployment configuration, not an admin setting (same reasoning as
/// <see cref="HealthProbeOptions"/>). The alert rules divide by the thresholds
/// the backend publishes, so they carry no number of their own except the
/// size ratios (70 % / 85 %) — DEPLOY_RUNBOOK §M.5.
/// </summary>
public sealed class DatabaseStorageMonitorOptions
{
    public const string SectionName = "DatabaseStorageMonitor";

    /// <summary>SQL Server Express caps the data files of one database at 10 GB (10 240 MB).</summary>
    public const long ExpressDataLimitMegabytes = 10_240;

    /// <summary><c>SERVERPROPERTY('EngineEdition')</c> of SQL Server Express.</summary>
    public const int ExpressEngineEdition = 4;

    public const int DefaultIntervalSeconds = 300;
    public const int MinimumIntervalSeconds = 30;
    public const int DefaultFullBackupMaxAgeHours = 26;
    public const int DefaultLogBackupMaxAgeMinutes = 45;

    /// <summary>When false nothing is probed or published. Default true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often the size and the backup history are read. Default 300 s; the
    /// size grows by megabytes a day at the trial's volume, the backup alarms
    /// count hours.
    /// </summary>
    public int IntervalSeconds { get; set; } = DefaultIntervalSeconds;

    /// <summary>
    /// The data-size limit the alarm divides by, in MB. Empty: 10 240 on SQL
    /// Server Express, no limit (no series, no alarm) on any other edition.
    /// Set it to watch a non-Express server against its disk, or — on a test
    /// stack — to make the alarm fire on a small database.
    /// </summary>
    public long? DataLimitMegabytes { get; set; }

    /// <summary>
    /// Age of the newest scheduled full backup above which
    /// <c>db-backup-full-stale</c> fires. Default 26 h: the 24 h schedule plus
    /// two hours for a restart catching up.
    /// </summary>
    public int FullBackupMaxAgeHours { get; set; } = DefaultFullBackupMaxAgeHours;

    /// <summary>
    /// Age of the newest log backup above which <c>db-backup-log-stale</c>
    /// fires (only while the database is in FULL recovery). Default 45 min:
    /// three missed 15-minute log backups.
    /// </summary>
    public int LogBackupMaxAgeMinutes { get; set; } = DefaultLogBackupMaxAgeMinutes;
}
