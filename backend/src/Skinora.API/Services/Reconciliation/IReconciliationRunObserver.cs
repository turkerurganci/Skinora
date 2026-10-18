using Skinora.Shared.Enums;

namespace Skinora.API.Services.Reconciliation;

/// <summary>
/// T158 (i) — how each network's ledger reconciliation round ended (05 §3.3,
/// 08 §3.7.4). A round that cannot read the network's balances — its service
/// unreachable, a balance chunk timing out — is not a mismatch and raises no
/// <c>RECONCILIATION_MISMATCH</c>; without this signal it was only a warning
/// log line, and a network whose round failed every day looked like one
/// whose books were fine. The Prometheus implementation publishes it per
/// network for the <c>reconciliation-network-failed</c> admin alarm.
/// </summary>
public interface IReconciliationRunObserver
{
    /// <param name="network">The network whose round ended.</param>
    /// <param name="succeeded">False when the balances could not be read (the round aborted).</param>
    void Report(BlockchainNetwork network, bool succeeded);
}

/// <summary>Observer that records nothing (tests, hosts without metrics).</summary>
public sealed class NullReconciliationRunObserver : IReconciliationRunObserver
{
    public static readonly NullReconciliationRunObserver Instance = new();

    public void Report(BlockchainNetwork network, bool succeeded)
    {
    }
}
