using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the WP16 platform health probe (05 §4.4, 02 §3.3) once per
/// component health-state transition: when a Steam / blockchain sidecar crosses
/// the consecutive-failure threshold (<c>DEGRADED</c>) or recovers afterwards
/// (<c>RECOVERED</c>). The Notifications consumer fans out an
/// <see cref="Skinora.Shared.Enums.NotificationType.ADMIN_PLATFORM_OUTAGE"/>
/// in-app alert to every admin so a degraded dependency is visible on the admin
/// inbox alongside the <c>PLATFORM_OUTAGE_DETECTED</c> audit row.
/// </summary>
/// <remarks>
/// The probe also freezes the component's active timeouts automatically on the
/// same edge (backlog WP1/T50, 02 §3.3) — this event is the notification half,
/// so a recipient learns about an outage the platform has already reacted to.
/// The admin's manual maintenance freeze (WP7) remains available.
/// <see cref="Component"/> is a stable
/// string identifier (<c>"STEAM"</c> / <c>"BLOCKCHAIN"</c>) rather than a shared
/// enum so the event carries no enum-parity surface.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="Component">Health component: <c>"STEAM"</c> or <c>"BLOCKCHAIN"</c>.</param>
/// <param name="Status">Transition: <c>"DEGRADED"</c> or <c>"RECOVERED"</c>.</param>
/// <param name="ConsecutiveFailures">Consecutive failed probes at the time of the alert (0 on recovery).</param>
/// <param name="OccurredAt">UTC timestamp the transition was detected.</param>
public record PlatformOutageAlertEvent(
    Guid EventId,
    string Component,
    string Status,
    int ConsecutiveFailures,
    DateTime OccurredAt) : IDomainEvent;
