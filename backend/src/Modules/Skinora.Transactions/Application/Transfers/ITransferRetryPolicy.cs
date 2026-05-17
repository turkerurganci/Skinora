namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// Resolves the configured retry cadence for outbound transfers (T73,
/// 08 §3.3 "retry 3 deneme: 1dk, 5dk, 15dk"). The CSV lives in
/// <c>SystemSetting "blockchain.transfer_retry_intervals_minutes"</c> so an
/// operator can tune backoff without a redeploy; if the value is malformed
/// or empty, the policy falls back to the documented default.
/// </summary>
public interface ITransferRetryPolicy
{
    /// <summary>
    /// Total number of attempts the dispatcher is allowed to make on a single
    /// row before flipping it to <c>FAILED</c>. Equal to <c>Intervals.Count + 1</c>
    /// (the first attempt has no preceding interval).
    /// </summary>
    Task<int> GetMaxAttemptsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Delay applied <em>before</em> the next attempt after the
    /// <paramref name="retryCount"/>-th failure. Returns <c>null</c> if the
    /// policy has been exhausted (caller must transition to FAILED).
    /// </summary>
    Task<TimeSpan?> GetRetryDelayAsync(int retryCount, CancellationToken cancellationToken);
}
