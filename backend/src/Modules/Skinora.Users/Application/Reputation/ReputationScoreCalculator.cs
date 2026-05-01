namespace Skinora.Users.Application.Reputation;

/// <summary>
/// Default implementation of <see cref="IReputationScoreCalculator"/>.
/// Pulls thresholds via <see cref="IReputationThresholdsProvider"/> on each
/// call so admin updates take effect without a process restart.
/// </summary>
public sealed class ReputationScoreCalculator : IReputationScoreCalculator
{
    private readonly IReputationThresholdsProvider _thresholds;

    public ReputationScoreCalculator(IReputationThresholdsProvider thresholds)
    {
        _thresholds = thresholds;
    }

    public async Task<decimal?> ComputeAsync(
        int completedTransactionCount,
        decimal? successfulTransactionRate,
        DateTime accountCreatedAt,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (successfulTransactionRate is null)
            return null;

        var thresholds = await _thresholds.GetAsync(cancellationToken);

        var accountAgeDays = (nowUtc - accountCreatedAt).TotalDays;
        if (accountAgeDays < thresholds.MinAccountAgeDays)
            return null;

        if (completedTransactionCount < thresholds.MinCompletedTransactions)
            return null;

        // 06 §3.1: ROUND(rate × 5, 1) with MidpointRounding.ToZero (truncation,
        // 06 §8.3 financial rounding rule).
        var raw = successfulTransactionRate.Value * 5m;
        return Math.Round(raw, 1, MidpointRounding.ToZero);
    }
}
