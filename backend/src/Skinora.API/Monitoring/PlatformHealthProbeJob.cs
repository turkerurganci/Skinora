using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;

namespace Skinora.API.Monitoring;

/// <summary>
/// Default <see cref="IPlatformHealthProbeJob"/> — probes each sidecar's
/// <c>/health</c> endpoint and, on an outage / recovery transition, writes a
/// <c>PLATFORM_OUTAGE_DETECTED</c> audit row and publishes a
/// <see cref="PlatformOutageAlertEvent"/> so the WP8 admin-broadcast consumer
/// alerts every admin (05 §4.4, 02 §3.3).
/// </summary>
/// <remarks>
/// <para>
/// Alert-only by design (owner decision, WP16): the probe never freezes
/// transactions automatically — it raises the alert and the admin applies the
/// maintenance freeze (WP7) if warranted. Edge-detection lives in
/// <see cref="PlatformHealthMonitorState"/> so a sustained outage alerts once,
/// not every poll.
/// </para>
/// <para>
/// The job owns its own unit of work (a standalone recurring job, not part of a
/// larger transaction), so it stages the audit + outbox rows and calls
/// <c>SaveChangesAsync</c> itself — mirroring <c>HeartbeatJob</c> / the
/// outbox-producing background jobs.
/// </para>
/// </remarks>
public sealed class PlatformHealthProbeJob : IPlatformHealthProbeJob
{
    private readonly ISidecarHealthClient _healthClient;
    private readonly PlatformHealthMonitorState _state;
    private readonly IOutboxService _outbox;
    private readonly IAuditLogger _auditLogger;
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly HealthProbeOptions _options;
    private readonly ILogger<PlatformHealthProbeJob> _logger;

    public PlatformHealthProbeJob(
        ISidecarHealthClient healthClient,
        PlatformHealthMonitorState state,
        IOutboxService outbox,
        IAuditLogger auditLogger,
        AppDbContext db,
        TimeProvider clock,
        IOptions<HealthProbeOptions> options,
        ILogger<PlatformHealthProbeJob> logger)
    {
        _healthClient = healthClient;
        _state = state;
        _outbox = outbox;
        _auditLogger = auditLogger;
        _db = db;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProbeAsync()
    {
        var transitions = 0;

        foreach (var component in PlatformComponents.All)
        {
            var healthy = await _healthClient.IsHealthyAsync(component, CancellationToken.None);
            if (healthy is null) continue; // not configured → not monitored

            var transition = _state.Record(component, healthy.Value, _options.FailureThreshold);
            if (transition == HealthTransition.None) continue;

            var status = transition == HealthTransition.Degraded ? "DEGRADED" : "RECOVERED";
            var consecutiveFailures = _state.ConsecutiveFailures(component);
            var now = _clock.GetUtcNow().UtcDateTime;

            // Audit row (SECURITY_EVENT) — durable record of the transition,
            // pairs 1:1 with the admin notification raised by the event below.
            await _auditLogger.LogAsync(
                new AuditLogEntry(
                    UserId: null,
                    ActorId: SeedConstants.SystemUserId,
                    ActorType: ActorType.SYSTEM,
                    Action: AuditAction.PLATFORM_OUTAGE_DETECTED,
                    EntityType: "PlatformHealth",
                    EntityId: component,
                    OldValue: null,
                    NewValue: JsonSerializer.Serialize(new
                    {
                        component,
                        status,
                        consecutiveFailures,
                    }),
                    IpAddress: null),
                CancellationToken.None);

            // Admin alert (WP8 admin-broadcast pattern) via the outbox.
            await _outbox.PublishAsync(
                new PlatformOutageAlertEvent(
                    EventId: Guid.NewGuid(),
                    Component: component,
                    Status: status,
                    ConsecutiveFailures: consecutiveFailures,
                    OccurredAt: now),
                CancellationToken.None);

            transitions++;
            _logger.LogWarning(
                "Platform health transition — component={Component} status={Status} consecutiveFailures={Failures}.",
                component, status, consecutiveFailures);
        }

        if (transitions > 0) await _db.SaveChangesAsync(CancellationToken.None);
    }
}
