using Prometheus;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.API.Monitoring;

/// <summary>
/// T152 — publishes <see cref="IOutboundQueueObserver"/> as Prometheus gauges,
/// one series per network. The <c>outbound-transfer-stuck</c> alert divides
/// the oldest pending age by the threshold published next to it, so the rule
/// carries no number of its own (same pattern as the Ethereum service's
/// <c>ethereum-finalized-stalled</c>).
/// </summary>
public sealed class PrometheusOutboundQueueObserver : IOutboundQueueObserver
{
    public static readonly Gauge PendingRows = Metrics.CreateGauge(
        "skinora_backend_outbound_pending_rows",
        "PENDING outbound transfer rows (payout, refunds, sweep) per network",
        new GaugeConfiguration { LabelNames = ["network"] });

    public static readonly Gauge OldestPendingSeconds = Metrics.CreateGauge(
        "skinora_backend_outbound_pending_oldest_seconds",
        "Age of the oldest PENDING outbound transfer row per network, seconds",
        new GaugeConfiguration { LabelNames = ["network"] });

    public static readonly Gauge AlarmSeconds = Metrics.CreateGauge(
        "skinora_backend_outbound_pending_alarm_seconds",
        "Oldest-pending age above which outbound-transfer-stuck fires, per network (networks without a threshold publish none)",
        new GaugeConfiguration { LabelNames = ["network"] });

    public void Report(BlockchainNetwork network, int pendingRows, double oldestPendingSeconds, double? alarmSeconds)
    {
        var label = network.ToString();
        PendingRows.WithLabels(label).Set(pendingRows);
        OldestPendingSeconds.WithLabels(label).Set(oldestPendingSeconds);
        if (alarmSeconds is { } threshold)
        {
            AlarmSeconds.WithLabels(label).Set(threshold);
        }
    }
}
