using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Reconciliation;

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

    /// <summary>
    /// T165 — the ledger ↔ chain mismatches a completed round found in one
    /// scope (05 §3.3 "Uyuşmazlık tespit edilirse admin'e critical alert").
    /// Before T165 a mismatch was an AuditLog row plus a SignalR banner for
    /// whoever had the admin panel open; the Prometheus implementation turns
    /// it into the <c>reconciliation-mismatch</c> money alarm (Telegram +
    /// e-mail), which resolves when a later round of that scope finds none.
    /// </summary>
    /// <remarks>
    /// Called only for a scope the round really compared: the deposit scope
    /// on every completed round (0 when no deposit is active) unless the
    /// snapshot left a deposit address out and the rest balanced (then it was
    /// not compared in full — a positive count is still reported), the hot / cold
    /// wallet only when its address is configured and was in the snapshot. An
    /// aborted round, or a scope not compared, reports nothing — the last
    /// value stays, so an unchecked wallet never reads as "balanced".
    /// </remarks>
    /// <param name="network">The network whose round completed.</param>
    /// <param name="scope">Deposit addresses, hot wallet or cold wallet.</param>
    /// <param name="mismatches">Token mismatches found (0 = balanced).</param>
    void ReportMismatches(BlockchainNetwork network, ReconciliationScope scope, int mismatches);
}

/// <summary>Observer that records nothing (tests, hosts without metrics).</summary>
public sealed class NullReconciliationRunObserver : IReconciliationRunObserver
{
    public static readonly NullReconciliationRunObserver Instance = new();

    public void Report(BlockchainNetwork network, bool succeeded)
    {
    }

    public void ReportMismatches(BlockchainNetwork network, ReconciliationScope scope, int mismatches)
    {
    }
}
