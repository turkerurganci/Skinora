using Prometheus;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.API.Monitoring;

/// <summary>
/// T172a doğrulama S4-4 — publishes <see cref="IOutboundDispatchPauseObserver"/>
/// as a Prometheus gauge (1 = paused, 0 = sending). The
/// <c>outbound-dispatch-paused</c> rule fires when it stays 1 for 30 minutes
/// (DEPLOY_RUNBOOK §N.8 step 6: the pause is turned off once every row is decided).
/// </summary>
public sealed class PrometheusOutboundDispatchPauseObserver : IOutboundDispatchPauseObserver
{
    public static readonly Gauge Paused = Metrics.CreateGauge(
        "skinora_backend_outbound_dispatch_paused",
        "1 while outbound.dispatch_paused holds every outgoing transfer (payout, refund, sweep; every network), 0 otherwise");

    public void Report(bool paused) => Paused.Set(paused ? 1 : 0);
}
