using Prometheus;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.GasFee;

namespace Skinora.API.Monitoring;

/// <summary>
/// T160b — publishes <see cref="IGasFeeFallbackObserver"/> as
/// <c>skinora_backend_gas_fee_fallback_charged_total{network, flow, reason, basis}</c>
/// (basis <c>measured</c> = the last measured estimate, <c>static</c> = the
/// setting). The <c>gas-fee-fallback-charged</c> alert fires on any increase.
/// </summary>
/// <remarks>
/// Every series of a refund-family flow is created at 0 when the observer is
/// built: <c>increase()</c> sees nothing on a counter whose first sample is
/// already 1, so a series born by the first fallback would never page.
/// </remarks>
public sealed class PrometheusGasFeeFallbackObserver : IGasFeeFallbackObserver
{
    public static readonly Counter FallbackCharged = Metrics.CreateCounter(
        "skinora_backend_gas_fee_fallback_charged_total",
        "Gas fees charged without a live estimate (refund deduction, payout split), by network, flow, reason and basis",
        new CounterConfiguration { LabelNames = ["network", "flow", "reason", "basis"] });

    private static readonly string[] Flows =
    [
        ChargedGasFeeResolver.RefundFlow,
        ChargedGasFeeResolver.HotWalletRefundFlow,
        ChargedGasFeeResolver.PayoutFlow,
    ];

    public PrometheusGasFeeFallbackObserver()
    {
        foreach (var network in Enum.GetValues<BlockchainNetwork>())
            foreach (var flow in Flows)
                foreach (var reason in Enum.GetValues<GasFeeFallbackReason>())
                    foreach (var basis in new[] { "measured", "static" })
                    {
                        FallbackCharged.WithLabels(network.ToString(), flow, Label(reason), basis).Inc(0);
                    }
    }

    void IGasFeeFallbackObserver.FallbackCharged(
        BlockchainNetwork network, string flow, GasFeeFallbackReason reason, bool measured) =>
        FallbackCharged.WithLabels(network.ToString(), flow, Label(reason), measured ? "measured" : "static").Inc();

    public static string Label(GasFeeFallbackReason reason) => reason switch
    {
        GasFeeFallbackReason.Unavailable => "unavailable",
        GasFeeFallbackReason.Rejected => "rejected",
        GasFeeFallbackReason.NoSender => "no_sender",
        _ => reason.ToString(),
    };
}
