using Prometheus;
using Skinora.API.Services.Reconciliation;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Reconciliation;

namespace Skinora.API.Monitoring;

/// <summary>
/// T158 (i) — publishes <see cref="IReconciliationRunObserver"/> per network:
/// <c>skinora_backend_reconciliation_last_run_failed{network}</c> (1 = the
/// last round could not read the balances, 0 = it ran) and the time of the
/// last completed round. The <c>reconciliation-network-failed</c> alert fires
/// on the first; it stays raised until a later round of that network
/// completes. A network that has not run since the backend started publishes
/// nothing (the alarm cannot see it — DEPLOY_RUNBOOK §J).
/// T165 — <c>skinora_backend_reconciliation_mismatches{network,scope}</c>: the
/// mismatches the last completed round found per scope, read by the
/// <c>reconciliation-mismatch</c> money alarm (critical, Telegram + e-mail).
/// </summary>
public sealed class PrometheusReconciliationRunObserver : IReconciliationRunObserver
{
    public static readonly Gauge LastRunFailed = Metrics.CreateGauge(
        "skinora_backend_reconciliation_last_run_failed",
        "1 when the last ledger reconciliation round of the network could not read its balances, 0 when it ran",
        new GaugeConfiguration { LabelNames = ["network"] });

    public static readonly Gauge LastSuccess = Metrics.CreateGauge(
        "skinora_backend_reconciliation_last_success_timestamp_seconds",
        "Unix time of the last completed ledger reconciliation round of the network",
        new GaugeConfiguration { LabelNames = ["network"] });

    public static readonly Gauge Mismatches = Metrics.CreateGauge(
        "skinora_backend_reconciliation_mismatches",
        "Ledger / chain mismatches found by the last completed reconciliation round of the network, per scope (DepositAddress, HotWallet, ColdWallet)",
        new GaugeConfiguration { LabelNames = ["network", "scope"] });

    private readonly TimeProvider _clock;

    public PrometheusReconciliationRunObserver(TimeProvider clock)
    {
        _clock = clock;
    }

    public void Report(BlockchainNetwork network, bool succeeded)
    {
        var label = network.ToString();
        LastRunFailed.WithLabels(label).Set(succeeded ? 0 : 1);
        if (succeeded)
        {
            LastSuccess.WithLabels(label).Set(_clock.GetUtcNow().ToUnixTimeSeconds());
        }
    }

    public void ReportMismatches(BlockchainNetwork network, ReconciliationScope scope, int mismatches) =>
        Mismatches.WithLabels(network.ToString(), scope.ToString()).Set(mismatches);
}
