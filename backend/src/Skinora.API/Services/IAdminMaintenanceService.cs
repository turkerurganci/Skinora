namespace Skinora.API.Services;

/// <summary>
/// Request body for <c>POST /admin/maintenance/freeze</c> (WP7 — 07 §9.31).
/// <c>Type</c> is one of the four activatable maintenance types
/// (<c>PLANNED_MAINTENANCE</c>, <c>PLATFORM_MAINTENANCE</c>,
/// <c>STEAM_OUTAGE</c>, <c>BLOCKCHAIN_DEGRADATION</c>); <c>NONE</c> is rejected
/// (use the resume endpoint to leave maintenance). <c>Message</c> and
/// <c>PlannedEnd</c> are optional — when omitted they persist as the
/// <c>"NONE"</c> sentinel and surface as JSON <c>null</c> on the public
/// endpoint. <c>PlannedEnd</c> must be ISO-8601 UTC when supplied.
/// </summary>
public sealed record MaintenanceFreezeRequest(
    string? Type,
    string? Message,
    string? PlannedEnd);

/// <summary>
/// Result body returned by the freeze/resume endpoints — the resulting
/// public maintenance state plus the number of transactions whose timeouts
/// were frozen (freeze) or resumed (resume) by the operation.
/// </summary>
public sealed record MaintenanceStateDto(
    bool Active,
    string? Type,
    string? Message,
    string? PlannedEnd,
    int AffectedTransactions);

public enum MaintenanceOperationStatus
{
    Ok,
    ValidationFailed,
}

/// <summary>Outcome of an <see cref="IAdminMaintenanceService"/> call.</summary>
public sealed record MaintenanceOperationOutcome(
    MaintenanceOperationStatus Status,
    MaintenanceStateDto? State,
    string? ErrorMessage)
{
    public static MaintenanceOperationOutcome Ok(MaintenanceStateDto state) =>
        new(MaintenanceOperationStatus.Ok, state, null);

    public static MaintenanceOperationOutcome Invalid(string message) =>
        new(MaintenanceOperationStatus.ValidationFailed, null, message);
}

/// <summary>
/// Admin maintenance/outage control (WP7 — 02 §3.3, 05 §4.4, 07 §9.31, §10.2).
/// </summary>
/// <remarks>
/// <para>
/// Lives at the API composition root because a single maintenance operation
/// spans three modules: it writes the Platform <c>platform.maintenance.*</c>
/// SystemSettings (the banner read-model behind <c>GET /platform/maintenance</c>),
/// drives the Transactions <see cref="Skinora.Transactions.Application.Timeouts.ITimeoutFreezeService"/>
/// bulk freeze/resume, and broadcasts the Realtime
/// <c>MaintenanceStatusChanged</c> push.
/// </para>
/// <para>
/// <b>Atomicity.</b> The settings write + audit row + bulk freeze/resume are
/// committed inside a single explicit DB transaction so the banner state and
/// the actual timeout-freeze state can never diverge (no split-brain). The
/// cache eviction and realtime push happen only after the commit succeeds.
/// </para>
/// </remarks>
public interface IAdminMaintenanceService
{
    /// <summary>
    /// Enter maintenance/outage mode: persist the four
    /// <c>platform.maintenance.*</c> settings, freeze the timeouts of every
    /// active transaction in the type's scope (none for
    /// <c>PLANNED_MAINTENANCE</c>), evict the public cache and broadcast the
    /// banner push.
    /// </summary>
    Task<MaintenanceOperationOutcome> FreezeAsync(
        Guid adminId,
        MaintenanceFreezeRequest request,
        string? ipAddress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Leave maintenance/outage mode: resume the timeouts frozen by the active
    /// reason, clear the four <c>platform.maintenance.*</c> settings, evict the
    /// public cache and broadcast the banner-cleared push. Idempotent when no
    /// maintenance is active.
    /// </summary>
    Task<MaintenanceOperationOutcome> ResumeAsync(
        Guid adminId,
        string? ipAddress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Evict the public maintenance cache and re-broadcast the current state.
    /// Invoked by the generic <c>PUT /admin/settings/:key</c> path after a
    /// <c>platform.maintenance.*</c> key is edited directly, so the banner read
    /// model never goes stale regardless of the write path. Does NOT freeze or
    /// resume timeouts — that is the dedicated freeze/resume endpoint's job.
    /// </summary>
    Task RefreshPublicStateAsync(CancellationToken cancellationToken);
}
