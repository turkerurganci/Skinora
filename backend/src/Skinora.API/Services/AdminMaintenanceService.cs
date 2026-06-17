using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Application.Settings;
using Skinora.Platform.Domain.Entities;
using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Timeouts;

namespace Skinora.API.Services;

/// <inheritdoc cref="IAdminMaintenanceService"/>
public sealed class AdminMaintenanceService : IAdminMaintenanceService
{
    private const string KeyActive = "platform.maintenance.active";
    private const string KeyType = "platform.maintenance.type";
    private const string KeyMessage = "platform.maintenance.message";
    private const string KeyPlannedEnd = "platform.maintenance.planned_end";

    /// <summary>06 §3.17 inactive sentinel for optional string fields.</summary>
    private const string NoneSentinel = "NONE";

    /// <summary>
    /// Maintenance types an admin may activate (07 §10.2). <c>NONE</c> is
    /// excluded — leaving maintenance goes through the resume endpoint.
    /// </summary>
    private static readonly IReadOnlySet<string> ActivatableTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "PLANNED_MAINTENANCE",
            "PLATFORM_MAINTENANCE",
            "STEAM_OUTAGE",
            "BLOCKCHAIN_DEGRADATION",
        };

    private static readonly string[] MaintenanceKeys =
        [KeyActive, KeyType, KeyMessage, KeyPlannedEnd];

    private readonly AppDbContext _db;
    private readonly ITimeoutFreezeService _freeze;
    private readonly IAuditLogger _audit;
    private readonly IMemoryCache _cache;
    private readonly INotificationRealtimePublisher _realtime;
    private readonly IPlatformPublicService _platformPublic;

    public AdminMaintenanceService(
        AppDbContext db,
        ITimeoutFreezeService freeze,
        IAuditLogger audit,
        IMemoryCache cache,
        INotificationRealtimePublisher realtime,
        IPlatformPublicService platformPublic)
    {
        _db = db;
        _freeze = freeze;
        _audit = audit;
        _cache = cache;
        _realtime = realtime;
        _platformPublic = platformPublic;
    }

    public async Task<MaintenanceOperationOutcome> FreezeAsync(
        Guid adminId,
        MaintenanceFreezeRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var type = request.Type?.Trim();
        if (string.IsNullOrEmpty(type) || !ActivatableTypes.Contains(type))
        {
            return MaintenanceOperationOutcome.Invalid(
                $"type must be one of {string.Join(", ", ActivatableTypes.OrderBy(t => t, StringComparer.Ordinal))}.");
        }

        var message = NormaliseToSentinel(request.Message);
        var plannedEnd = NormaliseToSentinel(request.PlannedEnd);

        // Reuse the shared SystemSetting string rules (notably the nvarchar(500)
        // column cap) so an admin can never persist a message the bootstrap/
        // validator would later reject or the column would truncate.
        var messageCheck = SystemSettingsValidator.Instance.ValidateSingle(
            KeyMessage, message, "string");
        if (!messageCheck.IsValid)
            return MaintenanceOperationOutcome.Invalid(messageCheck.ErrorMessage!);

        // Reuse the shared ISO-8601-or-NONE rule so an admin can never persist a
        // planned_end the bootstrap/validator would later reject.
        var plannedEndCheck = SystemSettingsValidator.Instance.ValidateSingle(
            KeyPlannedEnd, plannedEnd, "string");
        if (!plannedEndCheck.IsValid)
            return MaintenanceOperationOutcome.Invalid(plannedEndCheck.ErrorMessage!);

        var reason = FreezeReasonFor(type);

        var affected = await ApplyAsync(
            adminId,
            ipAddress,
            active: true,
            type: type,
            message: message,
            plannedEnd: plannedEnd,
            // PLANNED_MAINTENANCE is banner-only (07 §10.2): no timeout freeze.
            freezeReason: reason,
            resumeReason: null,
            cancellationToken);

        await BroadcastAsync(true, type, message, plannedEnd, cancellationToken);

        return MaintenanceOperationOutcome.Ok(new MaintenanceStateDto(
            Active: true,
            Type: type,
            Message: NullIfSentinel(message),
            PlannedEnd: NullIfSentinel(plannedEnd),
            AffectedTransactions: affected));
    }

    public async Task<MaintenanceOperationOutcome> ResumeAsync(
        Guid adminId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Resume the timeouts frozen by whatever reason is currently active.
        var currentType = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == KeyType)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        var resumeReason = currentType is null ? null : FreezeReasonFor(currentType);

        var affected = await ApplyAsync(
            adminId,
            ipAddress,
            active: false,
            type: NoneSentinel,
            message: NoneSentinel,
            plannedEnd: NoneSentinel,
            freezeReason: null,
            resumeReason: resumeReason,
            cancellationToken);

        await BroadcastAsync(false, NoneSentinel, NoneSentinel, NoneSentinel, cancellationToken);

        return MaintenanceOperationOutcome.Ok(new MaintenanceStateDto(
            Active: false,
            Type: null,
            Message: null,
            PlannedEnd: null,
            AffectedTransactions: affected));
    }

    public async Task RefreshPublicStateAsync(CancellationToken cancellationToken)
    {
        InvalidateCache();
        // GetMaintenanceAsync re-reads the four keys from the DB and repopulates
        // the 30s cache; broadcast the freshly-read state to every client.
        var state = await _platformPublic.GetMaintenanceAsync(cancellationToken);
        await _realtime.PublishMaintenanceStatusChangedAsync(
            new NotificationRealtimePayloads.MaintenanceStatusChanged(
                state.Active,
                state.Type,
                state.Message,
                ParsePlannedEnd(state.PlannedEnd)),
            cancellationToken);
    }

    /// <summary>
    /// Persist the four settings, run the bulk freeze or resume, then write the
    /// audit row — all inside a single explicit DB transaction so the banner and
    /// freeze states commit together (no split-brain). The audit envelope records
    /// the affected-transaction count (07 §9.31), which is only known after the
    /// bulk operation, so the audit row is written last. Returns the number of
    /// transactions affected. The cache eviction happens after commit; the
    /// realtime push is the caller's responsibility (post-commit).
    /// </summary>
    private async Task<int> ApplyAsync(
        Guid adminId,
        string? ipAddress,
        bool active,
        string type,
        string message,
        string plannedEnd,
        TimeoutFreezeReason? freezeReason,
        TimeoutFreezeReason? resumeReason,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var rows = await _db.Set<SystemSetting>()
            .Where(s => MaintenanceKeys.Contains(s.Key))
            .ToListAsync(cancellationToken);
        var byKey = rows.ToDictionary(r => r.Key, StringComparer.Ordinal);

        var oldValues = MaintenanceKeys.ToDictionary(
            k => k,
            k => byKey.TryGetValue(k, out var r) ? r.Value : null,
            StringComparer.Ordinal);

        ApplyValue(byKey, KeyActive, active ? "true" : "false", adminId);
        ApplyValue(byKey, KeyType, type, adminId);
        ApplyValue(byKey, KeyMessage, message, adminId);
        ApplyValue(byKey, KeyPlannedEnd, plannedEnd, adminId);

        var newValues = MaintenanceKeys.ToDictionary(
            k => k,
            k => byKey.TryGetValue(k, out var r) ? r.Value : null,
            StringComparer.Ordinal);

        // Flush the staged settings into the open transaction first. The bulk
        // freeze/resume below enlists in the same transaction but may early-out
        // without its own SaveChanges when nothing matches, so persist the
        // settings explicitly before the audit row is written.
        await _db.SaveChangesAsync(cancellationToken);

        var affected = 0;
        if (freezeReason is { } fr)
            affected = await _freeze.FreezeManyAsync(fr, cancellationToken);
        else if (resumeReason is { } rr)
            affected = await _freeze.ResumeManyAsync(rr, cancellationToken);

        // 07 §9.31 — the audit envelope records the old/new four settings plus
        // the number of transactions the freeze/resume touched. The count is
        // only known after the bulk operation, so the row is written here (still
        // inside the open transaction) rather than alongside the settings flush.
        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: null,
                ActorId: adminId,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.MAINTENANCE_MODE_CHANGED,
                EntityType: "Maintenance",
                EntityId: type,
                OldValue: JsonSerializer.Serialize(new { settings = oldValues }),
                NewValue: JsonSerializer.Serialize(new { settings = newValues, affectedTransactions = affected }),
                IpAddress: ipAddress),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        InvalidateCache();
        return affected;
    }

    private static void ApplyValue(
        IReadOnlyDictionary<string, SystemSetting> byKey,
        string key,
        string value,
        Guid adminId)
    {
        if (!byKey.TryGetValue(key, out var row))
            return;
        row.Value = value;
        row.IsConfigured = true;
        row.UpdatedByAdminId = adminId;
        // UpdatedAt is stamped by AppDbContext.UpdateAuditFields on SaveChanges.
    }

    private async Task BroadcastAsync(
        bool active, string type, string message, string plannedEnd, CancellationToken cancellationToken)
    {
        await _realtime.PublishMaintenanceStatusChangedAsync(
            new NotificationRealtimePayloads.MaintenanceStatusChanged(
                active,
                NullIfSentinel(type),
                NullIfSentinel(message),
                ParsePlannedEnd(plannedEnd)),
            cancellationToken);
    }

    private void InvalidateCache() => _cache.Remove(PlatformPublicService.MaintenanceCacheKey);

    /// <summary>
    /// Maps a maintenance <c>type</c> to its timeout-freeze reason (07 §10.2):
    /// PLATFORM_MAINTENANCE → all active, STEAM_OUTAGE → steam-bound,
    /// BLOCKCHAIN_DEGRADATION → payment step. PLANNED_MAINTENANCE and NONE map
    /// to <c>null</c> (no freeze).
    /// </summary>
    private static TimeoutFreezeReason? FreezeReasonFor(string type) => type switch
    {
        "PLATFORM_MAINTENANCE" => TimeoutFreezeReason.MAINTENANCE,
        "STEAM_OUTAGE" => TimeoutFreezeReason.STEAM_OUTAGE,
        "BLOCKCHAIN_DEGRADATION" => TimeoutFreezeReason.BLOCKCHAIN_DEGRADATION,
        _ => null,
    };

    private static string NormaliseToSentinel(string? raw)
    {
        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? NoneSentinel : trimmed;
    }

    private static string? NullIfSentinel(string? value) =>
        string.Equals(value, NoneSentinel, StringComparison.Ordinal) ? null : value;

    private static DateTime? ParsePlannedEnd(string? value)
    {
        if (string.IsNullOrEmpty(value) || string.Equals(value, NoneSentinel, StringComparison.Ordinal))
            return null;
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed.UtcDateTime
            : null;
    }
}
