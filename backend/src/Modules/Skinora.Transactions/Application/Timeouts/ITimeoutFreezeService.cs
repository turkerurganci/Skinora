using Skinora.Shared.Enums;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Timeouts;

/// <summary>
/// Timeout freeze/resume engine (T50 — 02 §3.3, 05 §4.4–§4.5, 09 §13.3).
/// Stamps the freeze tracking fields, cancels and re-issues the per-transaction
/// Hangfire jobs, and extends the four phase deadlines by the elapsed freeze
/// duration so users keep the relative time they had before the freeze.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reason matrix.</b> The four <see cref="TimeoutFreezeReason"/> values
/// are partitioned by scope: <c>MAINTENANCE</c>, <c>STEAM_OUTAGE</c> and
/// <c>BLOCKCHAIN_DEGRADATION</c> are platform-level and target groups of
/// active transactions (<see cref="TimeoutFreezeReasonScopes"/>);
/// <c>EMERGENCY_HOLD</c> is single-transaction only and goes through the
/// <c>Transaction</c> overloads <see cref="FreezeAsync"/> /
/// <see cref="ResumeAsync"/> from the T59 emergency-hold orchestrator.
/// </para>
/// <para>
/// <b>Atomicity (09 §13.3).</b> The single-tx overloads mutate the supplied
/// entity but do not commit — the caller composes them with the state machine
/// + audit + outbox in one <c>SaveChangesAsync</c>. The bulk overloads own
/// their unit of work and commit in a single <c>SaveChangesAsync</c> at the
/// end of the batch. Hangfire writes are out-of-band per the
/// <see cref="Skinora.Shared.BackgroundJobs.IBackgroundJobScheduler"/> contract.
/// </para>
/// <para>
/// <b>Authority (05 §4.4 + 06 §8.1).</b> Resume reads
/// <c>Transaction.TimeoutRemainingSeconds</c> as the source of truth for the
/// remaining payment window. <c>PaymentDeadline</c> is recomputed from
/// <c>now + remaining</c> on resume. Other phase deadlines (Accept,
/// TradeOfferToSeller, TradeOfferToBuyer) are bumped by the elapsed freeze
/// time directly because they are poller-driven and have no per-tx job.
/// </para>
/// </remarks>
public interface ITimeoutFreezeService
{
    /// <summary>
    /// Freezes a single transaction in place. Stamps <c>TimeoutFrozenAt</c> and
    /// <c>TimeoutFreezeReason</c> if not already frozen, captures
    /// <c>TimeoutRemainingSeconds</c> for <c>ITEM_ESCROWED</c>, and cancels the
    /// payment + warning Hangfire jobs (if any). Idempotent: re-freezing an
    /// already-frozen transaction preserves the original stamp and only
    /// re-runs the job cancel pass. Caller owns <c>SaveChangesAsync</c>.
    /// </summary>
    Task FreezeAsync(Transaction transaction, TimeoutFreezeReason reason, CancellationToken cancellationToken);

    /// <summary>
    /// Resumes a single frozen transaction. Extends each populated phase
    /// deadline by <c>now − TimeoutFrozenAt</c>, re-issues the
    /// <c>ITEM_ESCROWED</c> payment + warning Hangfire jobs from
    /// <c>TimeoutRemainingSeconds</c>, and clears <c>TimeoutFrozenAt</c>,
    /// <c>TimeoutFreezeReason</c> and <c>TimeoutRemainingSeconds</c>.
    /// Idempotent: a transaction that is not frozen is a no-op. Caller owns
    /// <c>SaveChangesAsync</c>.
    /// </summary>
    Task ResumeAsync(Transaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Bulk freeze of every active, non-held, not-already-frozen transaction
    /// whose <see cref="TransactionStatus"/> falls in the reason's scope
    /// (<see cref="TimeoutFreezeReasonScopes"/>). Owns the unit of work and
    /// commits in a single <c>SaveChangesAsync</c>. Returns the number of
    /// transactions affected. Throws <see cref="ArgumentException"/> for
    /// <c>EMERGENCY_HOLD</c>.
    /// </summary>
    Task<int> FreezeManyAsync(TimeoutFreezeReason reason, CancellationToken cancellationToken);

    /// <summary>
    /// Bulk resume of every transaction frozen with the given reason
    /// (<c>TimeoutFreezeReason == reason</c>). Owns the unit of work. Returns
    /// the number of transactions resumed. Throws
    /// <see cref="ArgumentException"/> for <c>EMERGENCY_HOLD</c>.
    /// </summary>
    Task<int> ResumeManyAsync(TimeoutFreezeReason reason, CancellationToken cancellationToken);
}
