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
    /// Failed attempts after which the admin alert is raised — once, on the
    /// attempt that reaches it (06 §3.18 retry semantiği). The row keeps being
    /// retried afterwards at the capped backoff (T150): an event whose handler
    /// needs another network's service — a monitor start, a post-cancel start —
    /// must still go through when that service comes back.
    /// </summary>
    public int MaxRetryCount { get; set; } = 5;

    /// <summary>
    /// First retry delay of a failed row, in seconds; doubles per failure
    /// (T150 — <c>OutboxNoBackoffOrDeadLetter</c>). 5 s · 2^(n−1): 5, 10, 20,
    /// 40, 80, … seconds.
    /// </summary>
    public int RetryBackoffBaseSeconds { get; set; } = 5;

    /// <summary>
    /// Ceiling of the retry delay, in seconds (default 15 min). A row that keeps
    /// failing is retried at most this often, so a configured-but-unreachable
    /// network costs one attempt per row per 15 minutes instead of one per poll.
    /// </summary>
    public int RetryBackoffMaxSeconds { get; set; } = 900;

    /// <summary>
    /// Batch slots kept for due FAILED rows even when fresh PENDING rows fill
    /// the batch, so retries cannot starve behind a sustained stream of new
    /// events. Fresh rows are dispatched first either way (T150).
    /// </summary>
    public int RetrySlotsPerBatch { get; set; } = 10;

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
