using Prometheus;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.API.Monitoring;

/// <summary>
/// T169a — publishes <see cref="IDepositRefundLimitObserver"/> as Prometheus
/// gauges, one series per network. The <c>deposit-refund-daily-limit-held</c>
/// rule fires while <see cref="HeldRows"/> is above 0; the used and limit
/// gauges say how close the network is (DEPLOY_RUNBOOK §J).
/// </summary>
public sealed class PrometheusDepositRefundLimitObserver : IDepositRefundLimitObserver
{
    public static readonly Gauge HeldRows = Metrics.CreateGauge(
        "skinora_backend_deposit_refund_limit_held_rows",
        "Refunds held by the network's deposit refund daily limit (PENDING, waiting for the 24 h window or an admin release)",
        new GaugeConfiguration { LabelNames = ["network"] });

    public static readonly Gauge UsedUsdt = Metrics.CreateGauge(
        "skinora_backend_deposit_refund_limit_used_usdt",
        "USDT the network's refunds sent from deposit addresses in the last 24 hours",
        new GaugeConfiguration { LabelNames = ["network"] });

    public static readonly Gauge LimitUsdt = Metrics.CreateGauge(
        "skinora_backend_deposit_refund_limit_usdt",
        "The network's deposit refund daily limit (blockchain.*.deposit_refund_daily_limit_usdt)",
        new GaugeConfiguration { LabelNames = ["network"] });

    public void Report(BlockchainNetwork network, int heldRows, decimal usedUsdt, decimal limitUsdt)
    {
        var label = network.ToString();
        HeldRows.WithLabels(label).Set(heldRows);
        UsedUsdt.WithLabels(label).Set((double)usedUsdt);
        LimitUsdt.WithLabels(label).Set((double)limitUsdt);
    }
}
