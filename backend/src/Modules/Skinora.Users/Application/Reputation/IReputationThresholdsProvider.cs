namespace Skinora.Users.Application.Reputation;

/// <summary>
/// Reads <c>reputation.min_account_age_days</c> and
/// <c>reputation.min_completed_transactions</c> from SystemSettings
/// (06 §3.17 / 02 §13 yetersiz veri eşikleri). Cached behaviour is up to
/// the implementation — callers should treat each call as cheap.
/// </summary>
public interface IReputationThresholdsProvider
{
    Task<ReputationThresholds> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Snapshot of the two reputation thresholds. Both are positive integers
/// (validator stage 2 enforces &gt; 0).
/// </summary>
/// <param name="MinAccountAgeDays">Account age (days) below which the score is null.</param>
/// <param name="MinCompletedTransactions">Completed transaction count below which the score is null.</param>
public sealed record ReputationThresholds(int MinAccountAgeDays, int MinCompletedTransactions);
