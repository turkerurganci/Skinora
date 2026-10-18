using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Networks;

namespace Skinora.API.Monitoring;

/// <summary>
/// <see cref="IBlockchainNetworkHealth"/> over the probe's in-memory state
/// (T148): a network is degraded while its blockchain component is in a
/// detected outage.
/// </summary>
/// <remarks>
/// T151 (kod düzeyi kapı): a network whose service must declare that it can
/// see payments (<see cref="PlatformComponents.RequiresPaymentCapability"/> —
/// Ethereum) is open for new transactions only while the LAST probe found it
/// healthy, which for such a network includes <c>acceptsPayments: true</c> in
/// its <c>/health</c> body (<see cref="SidecarHealthClient"/>). Before the
/// first probe, after a failed one, or while the service says it cannot see
/// payments, it stays out of <c>supportedNetworks</c> — the outage threshold
/// (which freezes timeouts and alerts) is not waited for.
/// </remarks>
public sealed class ProbeBlockchainNetworkHealth : IBlockchainNetworkHealth
{
    private readonly PlatformHealthMonitorState _state;

    public ProbeBlockchainNetworkHealth(PlatformHealthMonitorState state)
    {
        _state = state;
    }

    public bool IsDegraded(BlockchainNetwork network) =>
        _state.IsInOutage(PlatformComponents.BlockchainFor(network));

    public bool AcceptsNewTransactions(BlockchainNetwork network)
    {
        var component = PlatformComponents.BlockchainFor(network);
        if (_state.IsInOutage(component)) return false;
        return !PlatformComponents.RequiresPaymentCapability(network)
            || _state.LastProbeHealthy(component) == true;
    }
}

/// <summary>Stable component identifiers probed by the WP16 health monitor.</summary>
public static class PlatformComponents
{
    public const string Steam = "STEAM";

    /// <summary>
    /// TRON's blockchain service (T148 — health is per network, 05 §3.3a
    /// "Sağlık ve dondurma"; before T148 this component was the single
    /// <c>BLOCKCHAIN</c>).
    /// </summary>
    public const string BlockchainTron = "BLOCKCHAIN_TRON";

    /// <summary>Ethereum's blockchain service (T148). Unmonitored while it has no base URL.</summary>
    public const string BlockchainEthereum = "BLOCKCHAIN_ETHEREUM";

    /// <summary>The components the probe sweeps each run.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Steam, BlockchainTron, BlockchainEthereum };

    /// <summary>The health component of <paramref name="network"/>'s blockchain service.</summary>
    public static string BlockchainFor(BlockchainNetwork network) => network switch
    {
        BlockchainNetwork.TRON => BlockchainTron,
        BlockchainNetwork.ETHEREUM => BlockchainEthereum,
        _ => throw new ArgumentOutOfRangeException(nameof(network), network, "No health component for this network."),
    };

    /// <summary>
    /// T151 (kod düzeyi kapı) — networks whose service must publish
    /// <c>acceptsPayments: true</c> in its <c>/health</c> body to count as
    /// healthy and to be offered for new transactions. Ethereum: its service
    /// grew in steps (T150 skeleton answered <c>monitor/start</c> with 501), and
    /// a backend pointed at a service that cannot see payments would show
    /// deposit addresses nobody watches. TRON's service predates the flag and is
    /// complete, so it is not asked for it.
    /// </summary>
    public static bool RequiresPaymentCapability(BlockchainNetwork network) =>
        network == BlockchainNetwork.ETHEREUM;

    /// <summary>The network a blockchain component stands for; <c>false</c> for <see cref="Steam"/>.</summary>
    public static bool TryGetNetwork(string component, out BlockchainNetwork network)
    {
        switch (component)
        {
            case BlockchainTron:
                network = BlockchainNetwork.TRON;
                return true;
            case BlockchainEthereum:
                network = BlockchainNetwork.ETHEREUM;
                return true;
            default:
                network = default;
                return false;
        }
    }
}

/// <summary>The health-state transition produced by recording a probe result.</summary>
public enum HealthTransition
{
    /// <summary>No state change — nothing to alert on.</summary>
    None,

    /// <summary>Component just crossed the consecutive-failure threshold.</summary>
    Degraded,

    /// <summary>Component recovered after having been declared degraded.</summary>
    Recovered,
}

/// <summary>
/// In-memory per-component health state for the WP16 probe (singleton). Tracks
/// consecutive failures and the current outage flag so the probe alerts exactly
/// once on the healthy → degraded edge and once on degraded → healthy, never on
/// every failing poll.
/// </summary>
/// <remarks>
/// Single-instance MVP state (consistent with the in-memory rate-limiter store):
/// a multi-instance deployment would need a shared store, which is the explicitly
/// post-MVP Redis scale-out (PRE_F6_PLAN §3). On a backend restart the state
/// resets — the first post-restart probe sweep re-establishes it, at worst
/// re-alerting once for a still-degraded component, which is acceptable.
/// </remarks>
public sealed class PlatformHealthMonitorState
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ComponentHealth> _state =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Records a probe result for <paramref name="component"/> and returns the
    /// resulting state transition (edge-detected).
    /// </summary>
    public HealthTransition Record(string component, bool healthy, int failureThreshold)
    {
        lock (_lock)
        {
            if (!_state.TryGetValue(component, out var health))
            {
                health = new ComponentHealth();
                _state[component] = health;
            }

            health.LastHealthy = healthy;
            if (healthy)
            {
                var wasOutage = health.InOutage;
                health.ConsecutiveFailures = 0;
                health.InOutage = false;
                return wasOutage ? HealthTransition.Recovered : HealthTransition.None;
            }

            health.ConsecutiveFailures++;
            if (!health.InOutage && health.ConsecutiveFailures >= failureThreshold)
            {
                health.InOutage = true;
                return HealthTransition.Degraded;
            }

            return HealthTransition.None;
        }
    }

    /// <summary>
    /// Undoes the in-memory effect of a <see cref="HealthTransition"/> that was
    /// returned by <see cref="Record"/> but whose durable alert (audit + outbox)
    /// failed to persist. Restoring the <c>InOutage</c> flag lets the next probe
    /// re-detect the same edge instead of silently swallowing the alert
    /// (WP16 — the singleton survives a Hangfire retry / next cron tick while the
    /// rolled-back DbContext does not). The consecutive-failure counter is left
    /// as-is: a still-degraded component re-crosses the threshold on the next
    /// failing probe, and a recovered component re-reports on the next healthy
    /// probe.
    /// </summary>
    public void Revert(string component, HealthTransition transition)
    {
        if (transition == HealthTransition.None) return;
        lock (_lock)
        {
            if (!_state.TryGetValue(component, out var health)) return;
            // Degraded set InOutage=true → clear it so the next failing probe
            // re-detects Degraded. Recovered cleared it → set it back so the next
            // healthy probe re-detects Recovered.
            health.InOutage = transition == HealthTransition.Recovered;
        }
    }

    /// <summary>
    /// True while <paramref name="component"/> is in a detected outage — the
    /// degraded edge fired and no recovery has been recorded since (T148: the
    /// network availability check reads this).
    /// </summary>
    public bool IsInOutage(string component)
    {
        lock (_lock)
        {
            return _state.TryGetValue(component, out var health) && health.InOutage;
        }
    }

    /// <summary>Current consecutive-failure count for the component (0 if unknown/healthy).</summary>
    public int ConsecutiveFailures(string component)
    {
        lock (_lock)
        {
            return _state.TryGetValue(component, out var health)
                ? health.ConsecutiveFailures
                : 0;
        }
    }

    /// <summary>
    /// The result of the most recent probe of <paramref name="component"/>, or
    /// <c>null</c> when it has not been probed since the backend started (T151).
    /// </summary>
    public bool? LastProbeHealthy(string component)
    {
        lock (_lock)
        {
            return _state.TryGetValue(component, out var health) ? health.LastHealthy : null;
        }
    }

    private sealed class ComponentHealth
    {
        public int ConsecutiveFailures;
        public bool InOutage;
        public bool? LastHealthy;
    }
}
