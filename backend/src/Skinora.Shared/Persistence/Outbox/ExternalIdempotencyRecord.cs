namespace Skinora.Shared.Persistence.Outbox;

/// <summary>
/// Receiver-side idempotency record for external service commands
/// (06 §3.21, 05 §5.1).
/// </summary>
/// <remarks>
/// <para>
/// When the platform invokes a sidecar or blockchain service it sends an
/// <c>X-Idempotency-Key</c> header (typically the originating
/// <c>EventId</c> or <c>TransactionId + action</c>). The receiver writes a
/// row keyed by <c>(ServiceName, IdempotencyKey)</c>. A replay of the same
/// key returns the previously stored result instead of re-running the side
/// effect (e.g. issuing the same trade offer twice).
/// </para>
/// <para>
/// <b>Lease semantics (06 §3.21):</b> while a request is being processed
/// the row is held in <see cref="ExternalIdempotencyStatus.in_progress"/>
/// with <see cref="LeaseExpiresAt"/> set in the future. If the request
/// crashes mid-flight a subsequent caller can detect that the lease has
/// expired (<c>LeaseExpiresAt &lt; UtcNow</c>) and atomically reclaim the
/// row by transitioning it to <see cref="ExternalIdempotencyStatus.failed"/>,
/// after which the normal <c>failed → in_progress</c> claim flow applies.
/// </para>
/// <para>
/// Status-dependent invariants enforced via CHECK constraint
/// (<see cref="Configurations.ExternalIdempotencyRecordConfiguration"/>):
/// <list type="bullet">
///   <item><c>in_progress</c>: <see cref="CompletedAt"/> NULL,
///         <see cref="ResultPayload"/> NULL,
///         <see cref="LeaseExpiresAt"/> NOT NULL.</item>
///   <item><c>completed</c>: <see cref="CompletedAt"/> NOT NULL.</item>
///   <item><c>failed</c>: <see cref="CompletedAt"/> NULL.</item>
/// </list>
/// </para>
/// </remarks>
public class ExternalIdempotencyRecord
{
    /// <summary>Primary key (IDENTITY long).</summary>
    public long Id { get; set; }

    /// <summary>
    /// The <c>X-Idempotency-Key</c> header value sent by the platform —
    /// usually the originating <c>EventId</c> or
    /// <c>TransactionId + action</c>.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// Receiver service name. Together with <see cref="IdempotencyKey"/>
    /// forms the row's UNIQUE constraint — the same key may be reused across
    /// independent receivers without collision.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Current request state. Stored as a CHECK-constrained string column
    /// rather than an integer enum to keep ad-hoc DB inspection readable
    /// (06 §3.21).
    /// </summary>
    public ExternalIdempotencyStatus Status { get; set; }
        = ExternalIdempotencyStatus.in_progress;

    /// <summary>
    /// Side-effect result (JSON). Returned verbatim on replay so callers see
    /// the same response as the original request. NULL while the request is
    /// in progress; may stay NULL for side-effect-free commands.
    /// </summary>
    public string? ResultPayload { get; set; }

    /// <summary>
    /// Lease expiry — set while <see cref="Status"/> is
    /// <see cref="ExternalIdempotencyStatus.in_progress"/>. Once
    /// <c>LeaseExpiresAt &lt; UtcNow</c> the row is considered stale and may
    /// be reclaimed by another caller.
    /// </summary>
    public DateTime? LeaseExpiresAt { get; set; }

    /// <summary>UTC timestamp of the first request.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC timestamp when the request reached <c>completed</c>.</summary>
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Allowed values for <see cref="ExternalIdempotencyRecord.Status"/>
/// (06 §3.21 — CHECK IN ('in_progress', 'completed', 'failed')).
/// Lower-case names match the on-disk representation so the
/// CHECK constraint can use the literal values directly.
/// </summary>
public enum ExternalIdempotencyStatus
{
    in_progress,
    completed,
    failed,
}
