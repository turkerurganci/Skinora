namespace Skinora.Platform.Application.Heartbeat;

/// <summary>
/// Operational tuning for the platform heartbeat. Bound from the
/// <c>Heartbeat</c> configuration section. Lives in Platform (not in
/// Skinora.Transactions) because <see cref="HeartbeatJob"/> writes to
/// <c>SystemHeartbeats</c>, a Platform-owned entity.
/// </summary>
public sealed class HeartbeatOptions
{
    public const string SectionName = "Heartbeat";

    /// <summary>How often the heartbeat self-reschedules. Default 30 seconds (05 §4.4).</summary>
    public int IntervalSeconds { get; set; } = 30;
}
