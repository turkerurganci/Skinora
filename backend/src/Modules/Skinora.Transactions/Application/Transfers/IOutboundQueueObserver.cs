using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// T152 — per-network visibility of the outbound queue (proje sahibi kararı
/// 2026-10-18). On a network whose signer is idempotent (Ethereum) a row is
/// never failed for a transient error, so a stalled network shows up here
/// instead: how many outbound rows wait <c>PENDING</c> and how old the oldest
/// is, next to the network's alarm threshold. The dispatcher reports every run
/// for every network; the API publishes it as Prometheus gauges and the
/// <c>outbound-transfer-stuck</c> rule alarms per network (not per row).
/// </summary>
public interface IOutboundQueueObserver
{
    /// <param name="network">The network the numbers belong to.</param>
    /// <param name="pendingRows">PENDING outbound rows (payout, refunds, sweep).</param>
    /// <param name="oldestPendingSeconds">Age of the oldest of them since it was queued; 0 when none.</param>
    /// <param name="alarmSeconds">
    /// The network's stall threshold (Ethereum: <c>blockchain.ethereum.fee_cap_wait_alarm_minutes</c>
    /// × 60, the same 3 hours as the fee-cap wait alarm); null for a network without one.
    /// </param>
    void Report(BlockchainNetwork network, int pendingRows, double oldestPendingSeconds, double? alarmSeconds);

    /// <summary>
    /// T165 — the T169 approval gate's queue, every run for every network: how
    /// many payout/refund rows wait for an admin approval and how long the oldest
    /// has waited (0 / 0 when the gate is open or nothing waits). Published as
    /// <c>skinora_backend_outbound_awaiting_approval_*</c>; the
    /// <c>outbound-awaiting-approval</c> money alarm tells the operator outside
    /// the panel (one notification per episode, reminder every 4 h).
    /// </summary>
    void ReportAwaitingApproval(BlockchainNetwork network, int awaitingRows, double oldestAwaitingSeconds);
}

/// <summary>Observer that records nothing (tests, hosts without metrics).</summary>
public sealed class NullOutboundQueueObserver : IOutboundQueueObserver
{
    public static readonly NullOutboundQueueObserver Instance = new();

    public void Report(BlockchainNetwork network, int pendingRows, double oldestPendingSeconds, double? alarmSeconds)
    {
    }

    public void ReportAwaitingApproval(BlockchainNetwork network, int awaitingRows, double oldestAwaitingSeconds)
    {
    }
}
