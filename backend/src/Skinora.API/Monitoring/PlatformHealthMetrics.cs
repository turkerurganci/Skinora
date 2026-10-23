using Prometheus;

namespace Skinora.API.Monitoring;

/// <summary>
/// T170 — the health probe's view of each component as metrics. Until T170 an
/// outage reached the admin panel (PLATFORM_OUTAGE_DETECTED audit + the admin
/// broadcast) and froze the network's timeouts, but nothing reached the
/// operator's Telegram / e-mail: a network whose blockchain service went
/// unhealthy at night stayed closed to new transactions until someone opened
/// the panel. Two series per probed component:
/// <list type="bullet">
///   <item><c>skinora_backend_platform_component_up{component}</c> — the raw
///     result of the latest probe (1 healthy, 0 not). Its average over a window
///     is the measured availability of that component (the "uptime" figure the
///     landing page showed as a constant until T166).</item>
///   <item><c>skinora_backend_blockchain_service_degraded{network}</c> — 1 while
///     the network is in a detected outage (the debounced state the freeze and
///     the "closed to new transactions" rule read), 0 otherwise. The money
///     alarm <c>blockchain-service-degraded</c> fires on it.</item>
/// </list>
/// A component that is not configured is not probed and publishes nothing, so
/// a deployment without Ethereum never reads as an Ethereum outage.
/// </summary>
public static class PlatformHealthMetrics
{
    public static readonly Gauge ComponentUp = Metrics.CreateGauge(
        "skinora_backend_platform_component_up",
        "Result of the latest health probe of a platform component (1 healthy, 0 unhealthy)",
        new GaugeConfiguration { LabelNames = ["component"] });

    public static readonly Gauge BlockchainServiceDegraded = Metrics.CreateGauge(
        "skinora_backend_blockchain_service_degraded",
        "1 while a network's blockchain service is in a detected outage (payment timeouts frozen, network closed to new transactions)",
        new GaugeConfiguration { LabelNames = ["network"] });

    /// <summary>Publishes the probe result and, for a blockchain component, the outage state.</summary>
    public static void Publish(string component, bool healthy, bool inOutage)
    {
        ComponentUp.WithLabels(component).Set(healthy ? 1 : 0);
        if (PlatformComponents.TryGetNetwork(component, out var network))
        {
            BlockchainServiceDegraded.WithLabels(network.ToString()).Set(inOutage ? 1 : 0);
        }
    }
}
