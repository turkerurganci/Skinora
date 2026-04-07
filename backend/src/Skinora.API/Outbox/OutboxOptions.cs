namespace Skinora.API.Outbox;

/// <summary>
/// Outbox dispatcher configuration bound from the <c>Outbox</c> section of
/// appsettings (T10).
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// Delay between two dispatcher iterations. The Hangfire chain
    /// re-schedules itself with this offset (09 §13.4 — saniye bazlı polling).
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum number of <c>OutboxMessage</c> rows processed in a single
    /// dispatcher iteration. Bounded so the lock lease and overall iteration
    /// cost stay predictable (09 §13.4 lock lease kuralı).
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Maximum retries before a row is parked in <c>FAILED</c> and an admin
    /// alert is raised (06 §3.18 retry semantiği).
    /// </summary>
    public int MaxRetryCount { get; set; } = 5;

    /// <summary>
    /// Distributed lock acquire timeout. <see cref="TimeSpan.Zero"/> means
    /// "skip this iteration immediately if another dispatcher holds the
    /// lock" (09 §13.4 — Hangfire chain'i skip ile sessizce sonlanır).
    /// </summary>
    public int LockAcquireTimeoutSeconds { get; set; } = 0;

    /// <summary>
    /// Default lease duration for external idempotency claims
    /// (06 §3.21 — varsayılan 5 dakika). Sidecars override per-call.
    /// </summary>
    public int DefaultExternalIdempotencyLeaseSeconds { get; set; } = 300;
}
