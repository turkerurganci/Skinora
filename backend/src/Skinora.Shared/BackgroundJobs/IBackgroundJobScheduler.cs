using System;
using System.Linq.Expressions;

namespace Skinora.Shared.BackgroundJobs;

/// <summary>
/// Abstraction over the background job scheduler. Modules schedule, enqueue and
/// delete jobs through this interface so they do not take a direct dependency on
/// the underlying scheduler library (Hangfire today).
/// </summary>
/// <remarks>
/// <para>
/// All schedule/enqueue calls return a job identifier (string). Callers persist
/// this identifier on the relevant entity so they can later cancel the job
/// (e.g. <c>Transaction.PaymentTimeoutJobId</c> per 09 §13.3).
/// </para>
/// <para>
/// <b>Atomicity boundary (09 §13.3):</b> scheduling a job is NOT part of the
/// caller's database transaction. <see cref="Schedule{T}"/> writes to the
/// scheduler's own storage; if the caller's <c>SaveChangesAsync</c> later fails
/// the scheduled job becomes orphaned. Job handlers MUST therefore re-validate
/// the current entity state at runtime and no-op when the conditions no longer
/// hold (state validation pattern, 09 §13.3).
/// </para>
/// </remarks>
public interface IBackgroundJobScheduler
{
    /// <summary>
    /// Schedules a delayed one-shot job. The expression is serialized by the
    /// underlying scheduler so the dependency <typeparamref name="T"/> can be
    /// resolved from DI when the job runs.
    /// </summary>
    /// <returns>The scheduler-assigned job identifier.</returns>
    string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay);

    /// <summary>
    /// Enqueues a fire-and-forget job that runs as soon as a worker is
    /// available.
    /// </summary>
    /// <returns>The scheduler-assigned job identifier.</returns>
    string Enqueue<T>(Expression<Action<T>> methodCall);

    /// <summary>
    /// Cancels a job by id. Only effective for jobs that have not yet entered
    /// the processing state — jobs already running cannot be aborted (see
    /// 09 §13.3 "job handler state validation" rule for the required defensive
    /// no-op pattern in handlers).
    /// </summary>
    /// <returns><c>true</c> if the job was found and marked deleted.</returns>
    bool Delete(string jobId);
}
