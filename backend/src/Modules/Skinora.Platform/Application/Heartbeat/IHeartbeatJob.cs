namespace Skinora.Platform.Application.Heartbeat;

/// <summary>
/// Self-rescheduling Hangfire job that updates
/// <c>SystemHeartbeats.LastHeartbeat</c> on a fixed cadence so restart-recovery
/// (05 §4.4) can compute the outage window as <c>UtcNow - LastHeartbeat</c>.
/// Sub-minute polling uses the self-rescheduling delayed-job pattern (09 §13.4).
/// </summary>
public interface IHeartbeatJob
{
    Task TickAsync();
}
