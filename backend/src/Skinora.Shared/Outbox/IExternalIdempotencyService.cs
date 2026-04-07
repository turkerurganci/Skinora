namespace Skinora.Shared.Outbox;

/// <summary>
/// Receiver-side idempotency for outbound calls to sidecars and external
/// services (05 §5.1, 06 §3.21).
/// </summary>
/// <remarks>
/// <para>
/// Wraps the <c>ExternalIdempotencyRecords</c> table and exposes the three
/// state transitions defined by the spec: acquire-or-replay-or-block,
/// complete, fail. Together with provider-side idempotency this gives
/// effectively-once semantics for cross-process commands such as Steam trade
/// offer issuance and stablecoin transfers (05 §5.1).
/// </para>
/// </remarks>
public interface IExternalIdempotencyService
{
    /// <summary>
    /// Try to claim an idempotency key for processing.
    /// </summary>
    /// <param name="serviceName">
    /// Receiver service name — combined with <paramref name="idempotencyKey"/>
    /// to form the row's UNIQUE constraint (06 §3.21).
    /// </param>
    /// <param name="idempotencyKey">
    /// Caller-supplied key (typically the originating <c>EventId</c> or
    /// <c>TransactionId + action</c>).
    /// </param>
    /// <param name="leaseDuration">
    /// How long this caller's <c>in_progress</c> claim remains valid before
    /// another caller can reclaim a stale row (06 §3.21).
    /// </param>
    /// <returns>
    /// One of <see cref="ExternalIdempotencyAcquisition.Acquired"/>,
    /// <see cref="ExternalIdempotencyAcquisition.Replay"/> or
    /// <see cref="ExternalIdempotencyAcquisition.Blocked"/>.
    /// </returns>
    Task<ExternalIdempotencyAcquisition> AcquireAsync(
        string serviceName,
        string idempotencyKey,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark a previously acquired key as <c>completed</c> with an optional
    /// result payload (returned verbatim on replay).
    /// </summary>
    Task CompleteAsync(
        string serviceName,
        string idempotencyKey,
        string? resultPayload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark a previously acquired key as <c>failed</c> so a subsequent caller
    /// can claim and retry it.
    /// </summary>
    Task FailAsync(
        string serviceName,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of <see cref="IExternalIdempotencyService.AcquireAsync"/>.
/// </summary>
public abstract record ExternalIdempotencyAcquisition
{
    private ExternalIdempotencyAcquisition() { }

    /// <summary>
    /// Caller successfully acquired the lease and must perform the side
    /// effect, then call
    /// <see cref="IExternalIdempotencyService.CompleteAsync"/> or
    /// <see cref="IExternalIdempotencyService.FailAsync"/>.
    /// </summary>
    public sealed record Acquired : ExternalIdempotencyAcquisition;

    /// <summary>
    /// A previous request with the same key already <c>completed</c>; the
    /// stored <see cref="ResultPayload"/> should be returned to the caller
    /// without re-running the side effect.
    /// </summary>
    public sealed record Replay(string? ResultPayload) : ExternalIdempotencyAcquisition;

    /// <summary>
    /// Another caller currently holds an unexpired lease — the side effect
    /// is in progress elsewhere. Caller should wait/retry.
    /// </summary>
    public sealed record Blocked : ExternalIdempotencyAcquisition;
}
