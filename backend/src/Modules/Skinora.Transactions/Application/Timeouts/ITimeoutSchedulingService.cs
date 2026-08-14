namespace Skinora.Transactions.Application.Timeouts;

/// <summary>
/// Per-transaction Hangfire timeout job orchestrator (05 §4.4 "Aşama ayrımı",
/// 09 §13.3). Schedules and cancels the payment timeout (<c>PaymentTimeoutJobId</c>)
/// and the timeout warning (<c>TimeoutWarningJobId</c>) for transactions in
/// <c>SELLER_CONFIRMED</c>; non-payment timeouts (Accept / SellerConfirm /
/// Delivery) are enforced by <see cref="IDeadlineScannerJob"/> instead.
/// <para>
/// T124 added <see cref="ArmDeliveryDeadlineAsync"/> here even though the
/// delivery phase has no Hangfire job: what this service really owns is
/// "how long is phase X and when does it end", and keeping the delivery
/// window's SystemSetting lookup next to the payment window's keeps that
/// arithmetic in one place instead of a third copy inside a webhook handler.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomicity (09 §13.3):</b> the Hangfire write is NOT in the caller's DB
/// transaction. <see cref="SchedulePaymentTimeoutAsync"/> writes the job ids
/// onto the entity but does NOT call <c>SaveChangesAsync</c> — the caller owns
/// the unit of work and commits jobs + state in a single
/// <c>SaveChangesAsync</c> so a rollback also discards the orphan ids. If the
/// commit nevertheless fails after Hangfire stores the job, the orphan job
/// runs against a now-inconsistent state and the no-op pattern in
/// <see cref="ITimeoutExecutor"/> takes over.
/// </para>
/// <para>
/// <b>Reschedule authority (05 §4.4):</b> resume after freeze (T50) consumes
/// <c>Transaction.TimeoutRemainingSeconds</c> as the source of truth and
/// derives <c>PaymentDeadline</c> from it (06 §8.1).
/// </para>
/// </remarks>
public interface ITimeoutSchedulingService
{
    /// <summary>
    /// Schedules <c>PaymentTimeoutJobId</c> (delay = remainder of
    /// <c>PaymentDeadline</c> from now) and <c>TimeoutWarningJobId</c> (delay =
    /// <c>warningRatio × paymentTimeoutMinutes</c>). The transaction MUST be in
    /// <c>SELLER_CONFIRMED</c> with <c>PaymentDeadline</c> set; the caller is
    /// responsible for the state transition that produced that field.
    /// </summary>
    /// <returns>The two job ids written onto the entity (also persisted by the caller's <c>SaveChangesAsync</c>).</returns>
    Task<TimeoutJobIds> SchedulePaymentTimeoutAsync(
        Guid transactionId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the active payment timeout + warning Hangfire jobs (if any) and
    /// nulls the corresponding ids on the entity. Used by cancel / complete
    /// transitions and by freeze (T50). Idempotent: missing or already-deleted
    /// jobs are tolerated.
    /// </summary>
    Task CancelTimeoutJobsAsync(
        Guid transactionId, CancellationToken cancellationToken);

    /// <summary>
    /// Reschedules the payment timeout + warning using
    /// <c>TimeoutRemainingSeconds</c> as the authoritative remainder (05 §4.4 +
    /// 06 §8.1). Used by freeze-resume (T50) and by restart recovery to
    /// re-issue jobs after the storage clock has skipped forward.
    /// </summary>
    /// <param name="newPaymentDeadlineUtc">The new absolute deadline written onto the entity.</param>
    Task<TimeoutJobIds> ReschedulePaymentTimeoutAsync(
        Guid transactionId,
        TimeSpan remaining,
        DateTime newPaymentDeadlineUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// T124 — arms <c>DeliveryDeadline</c> for the seller's delivery window
    /// (02 §3.1 "adım 6–7", 03 §3.5) from the <c>delivery_timeout_minutes</c>
    /// SystemSetting. The transaction MUST already be in
    /// <c>PAYMENT_RECEIVED</c>; the caller owns the <c>ConfirmPayment</c>
    /// transition that produced that state.
    /// </summary>
    /// <remarks>
    /// No Hangfire job is scheduled: 05 §4.4 "Aşama ayrımı" enforces the
    /// delivery phase through <see cref="IDeadlineScannerJob"/>, and only the
    /// payment phase gets a per-transaction delayed job. Like
    /// <see cref="SchedulePaymentTimeoutAsync"/>, this writes onto the tracked
    /// entity and does NOT call <c>SaveChangesAsync</c>.
    /// </remarks>
    /// <returns>The absolute UTC deadline written onto the entity.</returns>
    Task<DateTime> ArmDeliveryDeadlineAsync(
        Guid transactionId, CancellationToken cancellationToken);
}

/// <summary>The two Hangfire job ids produced by <see cref="ITimeoutSchedulingService"/>.</summary>
public sealed record TimeoutJobIds(string PaymentTimeoutJobId, string? TimeoutWarningJobId);
