namespace Skinora.API.Monitoring;

/// <summary>Stable component identifiers probed by the WP16 health monitor.</summary>
public static class PlatformComponents
{
    public const string Steam = "STEAM";
    public const string Blockchain = "BLOCKCHAIN";

    /// <summary>The components the probe sweeps each run.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Steam, Blockchain };
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
