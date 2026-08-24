using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Timeouts;

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
/// Alerts <b>and</b> freezes (backlog WP1, owner decision 2026-08-24 —
/// supersedes the WP16 alert-only decision). 02 §3.3 promises that active
/// timeouts stop during a Steam outage or a blockchain degradation and that
/// detection is automatic; leaving the freeze to a human meant users were
/// charged a timeout for the platform's own outage whenever no admin was
/// awake. On the degraded edge the probe now calls
/// <see cref="ITimeoutFreezeService.FreezeManyAsync"/> for the component's
/// reason and on the recovery edge <see cref="ITimeoutFreezeService.ResumeManyAsync"/>.
/// Edge-detection lives in <see cref="PlatformHealthMonitorState"/> so a
/// sustained outage alerts (and freezes) once, not every poll, and the
/// <c>HealthProbe:FailureThreshold</c> consecutive-failure debounce already
/// keeps a single flaky probe from tripping it. The admin's manual
/// maintenance freeze (WP7) is unaffected and still available.
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
    private readonly ITimeoutFreezeService _freeze;
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly HealthProbeOptions _options;
    private readonly ILogger<PlatformHealthProbeJob> _logger;

    public PlatformHealthProbeJob(
        ISidecarHealthClient healthClient,
        PlatformHealthMonitorState state,
        IOutboxService outbox,
        IAuditLogger auditLogger,
        ITimeoutFreezeService freeze,
        AppDbContext db,
        TimeProvider clock,
        IOptions<HealthProbeOptions> options,
        ILogger<PlatformHealthProbeJob> logger)
    {
        _healthClient = healthClient;
        _state = state;
        _outbox = outbox;
        _auditLogger = auditLogger;
        _freeze = freeze;
        _db = db;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProbeAsync()
    {
        var applied = new List<(string Component, HealthTransition Transition)>();

        foreach (var component in PlatformComponents.All)
        {
            var healthy = await _healthClient.IsHealthyAsync(component, CancellationToken.None);
            if (healthy is null) continue; // not configured → not monitored

            var transition = _state.Record(component, healthy.Value, _options.FailureThreshold);
            if (transition == HealthTransition.None) continue;

            var status = transition == HealthTransition.Degraded ? "DEGRADED" : "RECOVERED";
            var consecutiveFailures = _state.ConsecutiveFailures(component);
            var now = _clock.GetUtcNow().UtcDateTime;

            // WP1 (T50) — apply the bulk timeout freeze/resume BEFORE staging the
            // alert rows. FreezeManyAsync/ResumeManyAsync own their own
            // SaveChanges on this same scoped AppDbContext; running them first
            // keeps that commit out of the alert unit of work, so the revert
            // path below still governs exactly the rows it staged. Both calls
            // are idempotent — freeze skips rows already frozen
            // (TimeoutFrozenAt == null filter) and resume matches on the same
            // reason — so a revert + re-detect on the next tick cannot
            // double-apply.
            var frozenOrResumed = await ApplyTimeoutFreezeAsync(component, transition);

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
                        // WP1 (T50) — how many transactions the automatic
                        // freeze/resume touched. null = the bulk call failed;
                        // the alert still stands and the admin can apply the
                        // manual maintenance freeze (WP7).
                        timeoutsAffected = frozenOrResumed,
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

            applied.Add((component, transition));
            _logger.LogWarning(
                "Platform health transition — component={Component} status={Status} "
                + "consecutiveFailures={Failures} timeoutsAffected={Affected}.",
                component, status, consecutiveFailures, frozenOrResumed);
        }

        if (applied.Count == 0) return;

        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The durable alert write failed (deadlock / connection drop / timeout)
            // → EF discarded the staged audit + outbox rows, but the in-memory
            // edge was already consumed. Roll the singleton back so the next probe
            // re-detects the same transition; otherwise the alert is lost for the
            // whole outage (the state would report "already alerted" forever).
            // Swallowed — the recurring job re-runs every ProbeCron tick.
            foreach (var (component, transition) in applied)
                _state.Revert(component, transition);

            _logger.LogError(
                ex,
                "Platform health probe could not persist {Count} outage transition(s) — "
                + "reverted in-memory state for re-detection on the next run.",
                applied.Count);
        }
    }

    /// <summary>
    /// WP1 (T50) — maps a component health edge onto the bulk timeout
    /// freeze/resume required by 02 §3.3 and applies it. Returns the number of
    /// transactions touched, or <c>null</c> when the component has no
    /// platform-level freeze reason or the bulk call failed.
    /// </summary>
    /// <remarks>
    /// A failure is logged and swallowed on purpose: the alert is the safety
    /// net and it must still reach the admins, who retain the manual WP7
    /// maintenance freeze. Failing the whole probe here would trade a partial
    /// outcome (alert without freeze) for no outcome at all.
    /// </remarks>
    private async Task<int?> ApplyTimeoutFreezeAsync(string component, HealthTransition transition)
    {
        var reason = component switch
        {
            PlatformComponents.Steam => (TimeoutFreezeReason?)TimeoutFreezeReason.STEAM_OUTAGE,
            PlatformComponents.Blockchain => TimeoutFreezeReason.BLOCKCHAIN_DEGRADATION,
            _ => null,
        };

        if (reason is null) return null;

        try
        {
            return transition == HealthTransition.Degraded
                ? await _freeze.FreezeManyAsync(reason.Value, CancellationToken.None)
                : await _freeze.ResumeManyAsync(reason.Value, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Automatic timeout {Action} failed for component={Component} reason={Reason} — "
                + "the outage alert still fires; apply the manual maintenance freeze if needed.",
                transition == HealthTransition.Degraded ? "freeze" : "resume",
                component,
                reason.Value);
            return null;
        }
    }
}
