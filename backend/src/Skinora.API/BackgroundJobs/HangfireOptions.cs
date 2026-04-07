namespace Skinora.API.BackgroundJobs;

/// <summary>
/// Hangfire configuration bound from the <c>Hangfire</c> section of
/// appsettings (T09).
/// </summary>
public sealed class HangfireOptions
{
    public const string SectionName = "Hangfire";

    /// <summary>
    /// Whether to mount the Hangfire dashboard at <see cref="DashboardPath"/>.
    /// Defaults to <c>true</c>; can be disabled per-environment.
    /// </summary>
    public bool DashboardEnabled { get; set; } = true;

    /// <summary>
    /// Dashboard mount path. Default <c>/hangfire</c>.
    /// </summary>
    public string DashboardPath { get; set; } = "/hangfire";

    /// <summary>
    /// SQL schema name for Hangfire's own tables. Default <c>HangFire</c>.
    /// </summary>
    public string SchemaName { get; set; } = "HangFire";

    /// <summary>
    /// Storage polling interval in seconds. Lower values reduce job pickup
    /// latency at the cost of database load.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Default automatic retry attempts applied to every job (09 §13.5).
    /// </summary>
    public int DefaultRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Number of Hangfire worker threads. <c>null</c> = library default
    /// (<c>Min(20, ProcessorCount * 5)</c>).
    /// </summary>
    public int? WorkerCount { get; set; }
}
