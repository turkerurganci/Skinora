namespace Skinora.API.BackgroundJobs.Timeouts;

/// <summary>
/// Restart-recovery service per 05 §4.4. On application startup it computes
/// the outage window (<c>UtcNow - SystemHeartbeats.LastHeartbeat</c>) and, if
/// it exceeds the configured threshold, extends every active transaction's
/// phase deadline by that window so users do not see their timeouts elapse
/// during a backend outage.
/// </summary>
public interface IRestartRecoveryService
{
    /// <summary>
    /// Runs the recovery pass synchronously and returns the outage window
    /// detected (<see cref="TimeSpan.Zero"/> when no extension was applied).
    /// Idempotent: calling twice in a row produces a no-op the second time
    /// because the heartbeat row is stamped at the end.
    /// </summary>
    Task<RestartRecoveryResult> RunAsync(CancellationToken cancellationToken);
}

/// <summary>Outcome of a single restart-recovery pass.</summary>
/// <param name="OutageWindow">Detected outage window (LastHeartbeat → UtcNow).</param>
/// <param name="ExtensionApplied">True when the threshold was exceeded and deadlines were bumped.</param>
/// <param name="ExtendedTransactionCount">Number of transactions whose deadlines were rewritten.</param>
/// <param name="RescheduledPaymentJobCount">Number of ITEM_ESCROWED transactions whose Hangfire jobs were re-issued.</param>
public sealed record RestartRecoveryResult(
    TimeSpan OutageWindow,
    bool ExtensionApplied,
    int ExtendedTransactionCount,
    int RescheduledPaymentJobCount);
