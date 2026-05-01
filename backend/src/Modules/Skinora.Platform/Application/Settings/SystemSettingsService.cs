using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;

namespace Skinora.Platform.Application.Settings;

/// <summary>
/// EF Core-backed implementation of 07 §9.8–§9.9. The catalog is the single
/// source of truth for the response shape — keys present in the DB but absent
/// from the catalog are excluded (defensive: future migrations that add a row
/// must also add catalog metadata, otherwise the row is invisible to the API
/// and SystemSettingsCatalogTests fails the build).
/// </summary>
public sealed class SystemSettingsService : ISystemSettingsService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly IAuditLogger _auditLogger;
    private readonly SystemSettingsValidator _validator;

    public SystemSettingsService(
        AppDbContext db, TimeProvider clock, IAuditLogger auditLogger)
        : this(db, clock, auditLogger, SystemSettingsValidator.Instance)
    {
    }

    internal SystemSettingsService(
        AppDbContext db,
        TimeProvider clock,
        IAuditLogger auditLogger,
        SystemSettingsValidator validator)
    {
        _db = db;
        _clock = clock;
        _auditLogger = auditLogger;
        _validator = validator;
    }

    public async Task<SettingsListResponse> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var byKey = rows.ToDictionary(r => r.Key, StringComparer.Ordinal);

        var items = new List<SettingItemDto>(SystemSettingsCatalog.All.Count);
        foreach (var meta in SystemSettingsCatalog.All)
        {
            if (!byKey.TryGetValue(meta.Key, out var row))
                continue;

            items.Add(new SettingItemDto(
                Key: row.Key,
                Value: row.Value,
                Category: meta.ApiCategory,
                Label: meta.Label,
                Description: row.Description,
                Unit: meta.Unit,
                ValueType: SystemSettingsCatalog.ValueTypeFor(row.DataType)));
        }

        return new SettingsListResponse(items);
    }

    public async Task<UpdateSettingOutcome> UpdateAsync(
        string key,
        UpdateSettingRequest request,
        Guid actorAdminId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || !SystemSettingsCatalog.Contains(key))
            return new UpdateSettingOutcome.NotFound(key);

        var setting = await _db.Set<SystemSetting>()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting is null)
            return new UpdateSettingOutcome.NotFound(key);

        var newValue = NormalizeValue(request.Value, setting.DataType);

        var single = _validator.ValidateSingle(key, newValue, setting.DataType);
        if (!single.IsValid)
            return new UpdateSettingOutcome.ValidationFailed(single.ErrorMessage!);

        // Cross-key invariants are evaluated against the *post-write* snapshot
        // so the new value is included. The current row hasn't been saved yet,
        // so we substitute it manually.
        var allRows = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var snapshot = allRows
            .Where(r => r.IsConfigured || r.Key == key)
            .ToDictionary(r => r.Key, r => r.Key == key ? newValue : r.Value, StringComparer.Ordinal);
        var cross = _validator.ValidateCrossKey(snapshot);
        if (!cross.IsValid)
            return new UpdateSettingOutcome.ValidationFailed(cross.ErrorMessage!);

        var oldValue = setting.Value;
        var wasConfigured = setting.IsConfigured;

        setting.Value = newValue;
        setting.IsConfigured = true;
        setting.UpdatedByAdminId = actorAdminId;
        // UpdatedAt is set by AppDbContext.UpdateAuditFields on SaveChanges.

        // 06 §3.20 — append-only AuditLog row in the same transaction. The
        // central IAuditLogger (T42, 09 §18.6) stages the row on the same
        // change tracker; SaveChangesAsync below commits both the SystemSetting
        // update and the audit row atomically.
        await _auditLogger.LogAsync(
            new AuditLogEntry(
                UserId: actorAdminId,
                ActorId: actorAdminId,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.SYSTEM_SETTING_CHANGED,
                EntityType: nameof(SystemSetting),
                EntityId: key,
                OldValue: JsonSerializer.Serialize(new { value = oldValue, isConfigured = wasConfigured }),
                NewValue: JsonSerializer.Serialize(new { value = newValue, isConfigured = true }),
                IpAddress: ipAddress),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new UpdateSettingOutcome.Success(new UpdateSettingResponse(
            Key: setting.Key,
            Value: setting.Value,
            UpdatedAt: setting.UpdatedAt));
    }

    /// <summary>
    /// Normalize tolerant inputs: trim strings, lowercase booleans. Numeric
    /// types are passed through verbatim — culture-aware parsing happens in
    /// the validator.
    /// </summary>
    private static string? NormalizeValue(string? raw, string dataType)
    {
        if (raw is null) return null;
        var trimmed = raw.Trim();
        return dataType switch
        {
            "bool" => trimmed.ToLowerInvariant(),
            _ => trimmed,
        };
    }
}
