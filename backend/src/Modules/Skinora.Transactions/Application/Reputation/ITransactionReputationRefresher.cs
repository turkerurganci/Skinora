namespace Skinora.Transactions.Application.Reputation;

/// <summary>
/// WP15 — shared orchestration for the post-terminal reputation projection.
/// Wraps <see cref="Skinora.Users.Application.Reputation.IReputationAggregator"/>
/// and <see cref="Skinora.Users.Application.Reputation.IUserCancelCooldownEvaluator"/>
/// so every terminal-transition caller (COMPLETED, CANCELLED_TIMEOUT,
/// Steam-driven CANCELLED_SELLER/BUYER, user-cancel) refreshes the denormalized
/// reputation fields the same way (06 §8.2 — "İşlem COMPLETED veya CANCELLED
/// olduğunda güncellenir").
/// </summary>
/// <remarks>
/// Both wrapped services query <c>Transaction</c>/<c>TransactionHistory</c> with
/// <c>AsNoTracking</c>, so the caller MUST have already flushed the terminal
/// status flip (and, for timeouts, the <c>TransactionHistory</c> row the
/// responsibility map reads) before calling. Mutates the tracked <c>User</c>
/// entities; the caller owns the surrounding <c>SaveChangesAsync</c>.
/// </remarks>
public interface ITransactionReputationRefresher
{
    /// <summary>
    /// Recomputes the denormalized reputation snapshot for both parties of a
    /// just-terminal transaction. When <paramref name="evaluateCooldown"/> is
    /// <c>true</c> (cancellation-class transitions) the cancel-cooldown rule is
    /// re-evaluated for both parties too — the evaluator's responsibility map
    /// internally skips the non-responsible party, so a stale cooldown is never
    /// stamped on the wrong side.
    /// </summary>
    /// <param name="sellerId">Always present.</param>
    /// <param name="buyerId">May be null (pre-accept seller cancel) — skipped when null.</param>
    /// <param name="evaluateCooldown"><c>true</c> for CANCELLED_*; <c>false</c> for COMPLETED.</param>
    Task RefreshAsync(
        Guid sellerId,
        Guid? buyerId,
        bool evaluateCooldown,
        CancellationToken cancellationToken);
}
