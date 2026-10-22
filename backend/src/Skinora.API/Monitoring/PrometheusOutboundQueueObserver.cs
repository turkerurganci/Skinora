using Microsoft.Extensions.Options;
using Prometheus;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.API.Monitoring;

/// <summary>
/// T158 (a) — the per-network PENDING outbound row count above which the
/// <c>outbound-transfer-backlog</c> alert fires (section <c>OutboundQueueAlarm</c>,
/// env <c>OutboundQueueAlarm__PendingRowsThreshold</c>). Default 25: more than
/// a whole dispatcher batch (20 rows per network per minute) waiting is not a
/// fee-cap minute or a slow block, it is a backlog — an outage piling rows up
/// as PENDING (Ethereum rows never turn FAILED on a transient error, T152
/// (xvii); TRON has no age threshold at all). Deployment configuration, not an
/// admin setting (öneriyle kaydedildi — ⚠, 11 §F8 "T158 kararları").
/// </summary>
public sealed class OutboundQueueAlarmOptions
{
    public const string SectionName = "OutboundQueueAlarm";

    public const int DefaultPendingRowsThreshold = 25;

    public int PendingRowsThreshold { get; set; } = DefaultPendingRowsThreshold;
}

/// <summary>
/// T152 — publishes <see cref="IOutboundQueueObserver"/> as Prometheus gauges,
/// one series per network. The <c>outbound-transfer-stuck</c> alert divides
/// the oldest pending age by the threshold published next to it, so the rule
/// carries no number of its own (same pattern as the Ethereum service's
/// <c>ethereum-finalized-stalled</c>). T158 (a) — the row count gets the same
/// treatment: <c>outbound-transfer-backlog</c> divides it by the published
/// <see cref="PendingRowsAlarmThreshold"/>.
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

    /// <summary>T158 (a) — PENDING row count above which outbound-transfer-backlog fires, per network.</summary>
    public static readonly Gauge PendingRowsAlarmThreshold = Metrics.CreateGauge(
        "skinora_backend_outbound_pending_rows_alarm_threshold",
        "PENDING outbound row count above which outbound-transfer-backlog fires, per network",
        new GaugeConfiguration { LabelNames = ["network"] });

    /// <summary>T165 — payout/refund rows held by the T169 approval gate, per network.</summary>
    public static readonly Gauge AwaitingApprovalRows = Metrics.CreateGauge(
        "skinora_backend_outbound_awaiting_approval_rows",
        "PENDING payout/refund rows waiting for an admin approval (T169 gate closed), per network",
        new GaugeConfiguration { LabelNames = ["network"] });

    /// <summary>T165 — how long the oldest held row has waited, per network.</summary>
    public static readonly Gauge AwaitingApprovalOldestSeconds = Metrics.CreateGauge(
        "skinora_backend_outbound_awaiting_approval_oldest_seconds",
        "Age of the oldest payout/refund row waiting for an admin approval, per network, seconds",
        new GaugeConfiguration { LabelNames = ["network"] });

    private readonly int _rowsThreshold;

    public PrometheusOutboundQueueObserver()
        : this(Options.Create(new OutboundQueueAlarmOptions()))
    {
    }

    public PrometheusOutboundQueueObserver(IOptions<OutboundQueueAlarmOptions> options)
    {
        var configured = options.Value.PendingRowsThreshold;
        // A non-positive value would make the alarm fire on an empty queue
        // (or divide by zero): fall back to the default rather than page.
        _rowsThreshold = configured > 0 ? configured : OutboundQueueAlarmOptions.DefaultPendingRowsThreshold;
    }

    public void Report(BlockchainNetwork network, int pendingRows, double oldestPendingSeconds, double? alarmSeconds)
    {
        var label = network.ToString();
        PendingRows.WithLabels(label).Set(pendingRows);
        OldestPendingSeconds.WithLabels(label).Set(oldestPendingSeconds);
        PendingRowsAlarmThreshold.WithLabels(label).Set(_rowsThreshold);
        if (alarmSeconds is { } threshold)
        {
            AlarmSeconds.WithLabels(label).Set(threshold);
        }
    }

    public void ReportAwaitingApproval(BlockchainNetwork network, int awaitingRows, double oldestAwaitingSeconds)
    {
        var label = network.ToString();
        AwaitingApprovalRows.WithLabels(label).Set(awaitingRows);
        AwaitingApprovalOldestSeconds.WithLabels(label).Set(oldestAwaitingSeconds);
    }
}
