using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Networks;

namespace Skinora.API.Monitoring;

/// <summary>
/// <see cref="IBlockchainNetworkHealth"/> over the probe's in-memory state
/// (T148): a network is degraded while its blockchain component is in a
/// detected outage.
/// </summary>
public sealed class ProbeBlockchainNetworkHealth : IBlockchainNetworkHealth
{
    private readonly PlatformHealthMonitorState _state;

    public ProbeBlockchainNetworkHealth(PlatformHealthMonitorState state)
    {
        _state = state;
    }

    public bool IsDegraded(BlockchainNetwork network) =>
        _state.IsInOutage(PlatformComponents.BlockchainFor(network));
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

    private sealed class ComponentHealth
    {
        public int ConsecutiveFailures;
        public bool InOutage;
    }
}
