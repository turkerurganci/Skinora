namespace Skinora.Users.Application.Reputation;

/// <summary>
/// Read-path composite reputation score calculator (06 §3.1, 02 §13).
/// Computes <c>ROUND(SuccessfulTransactionRate × 5, 1)</c> when both
/// account-age and completed-transaction thresholds are satisfied,
/// otherwise returns <c>null</c> ("Yeni kullanıcı" UI state).
/// </summary>
/// <remarks>
/// The score is intentionally not denormalized — thresholds are admin-tunable
/// SystemSettings, so storing the value would drift the moment an admin edits
/// <c>reputation.min_account_age_days</c> or <c>reputation.min_completed_transactions</c>.
/// </remarks>
public interface IReputationScoreCalculator
{
    /// <summary>
    /// Compute the composite score for a user snapshot.
    /// </summary>
    /// <param name="completedTransactionCount">User.CompletedTransactionCount (denormalized, 06 §8.2).</param>
    /// <param name="successfulTransactionRate">User.SuccessfulTransactionRate (denormalized, fraction 0..1 or null).</param>
    /// <param name="accountCreatedAt">User.CreatedAt — used for account-age threshold.</param>
    /// <param name="nowUtc">Current UTC instant — caller injects to keep the helper deterministic.</param>
    /// <returns><c>decimal</c> in <c>[0.0, 5.0]</c> with 1 decimal place, or <c>null</c> if any threshold fails.</returns>
    Task<decimal?> ComputeAsync(
        int completedTransactionCount,
        decimal? successfulTransactionRate,
        DateTime accountCreatedAt,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}
