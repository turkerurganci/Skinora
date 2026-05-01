namespace Skinora.Users.Application.Reputation;

/// <summary>
/// Recomputes the denormalized reputation fields on
/// <see cref="Skinora.Users.Domain.Entities.User"/>
/// (06 §3.1 + 06 §8.2) — <c>CompletedTransactionCount</c> and
/// <c>SuccessfulTransactionRate</c>. Designed to be called by future
/// transaction state-machine hooks (T44+) on COMPLETED / CANCELLED_*
/// transitions; idempotent so safe to invoke from outbox event handlers and
/// reconciliation jobs.
/// </summary>
public interface IReputationAggregator
{
    /// <summary>
    /// Re-derive and persist the denormalized reputation fields for the given
    /// user from the current <c>Transaction</c> table state. Caller is
    /// responsible for the surrounding unit-of-work — this method mutates the
    /// tracked <c>User</c> entity but does NOT call <c>SaveChangesAsync</c>.
    /// </summary>
    /// <returns>The recomputed snapshot, mainly for logging / assertions.</returns>
    Task<ReputationSnapshot> RecomputeAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Result of <see cref="IReputationAggregator.RecomputeAsync"/>.</summary>
/// <param name="CompletedTransactionCount">Raw COMPLETED count (wash filter NOT applied).</param>
/// <param name="SuccessfulTransactionRate">
/// fraction (0..1) or null when the responsibility-attributed denominator
/// after the wash trading filter is zero (insufficient data).
/// </param>
public sealed record ReputationSnapshot(int CompletedTransactionCount, decimal? SuccessfulTransactionRate);
