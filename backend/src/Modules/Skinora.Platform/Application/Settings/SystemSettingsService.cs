using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Application.Wallets;
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
    private readonly ISettingChangePropagator _propagator;
    private readonly SystemSettingsValidator _validator;
    private readonly IPlatformWalletAddressProvider _walletAddresses;

    /// <summary>
    /// Convenience constructor used by tests and non-API hosts — propagation is
    /// a no-op (no cron-scheduled jobs to re-register).
    /// </summary>
    public SystemSettingsService(
        AppDbContext db, TimeProvider clock, IAuditLogger auditLogger)
        : this(db, clock, auditLogger, NoOpSettingChangePropagator.Instance, SystemSettingsValidator.Instance)
    {
    }

    /// <summary>
    /// DI constructor — the API host supplies a real
    /// <see cref="ISettingChangePropagator"/> (cron job re-registration, WP14).
    /// </summary>
    public SystemSettingsService(
        AppDbContext db,
        TimeProvider clock,
        IAuditLogger auditLogger,
        ISettingChangePropagator propagator,
        IPlatformWalletAddressProvider walletAddresses)
        : this(db, clock, auditLogger, propagator, SystemSettingsValidator.Instance, walletAddresses)
    {
    }

    internal SystemSettingsService(
        AppDbContext db,
        TimeProvider clock,
        IAuditLogger auditLogger,
        ISettingChangePropagator propagator,
        SystemSettingsValidator validator,
        IPlatformWalletAddressProvider? walletAddresses = null)
    {
        _db = db;
        _clock = clock;
        _auditLogger = auditLogger;
        _propagator = propagator;
        _validator = validator;
        // Hosts without wallet configuration (tests, tooling) see the
        // env-sourced rows as unset rather than crashing on resolve.
        _walletAddresses = walletAddresses ?? UnsetPlatformWalletAddresses.Instance;
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
            // Env-sourced entries have no SystemSetting row at all: the panel
            // still lists them so an operator can see where the deployment
            // points, but the value comes from configuration and the update
            // endpoint refuses them (05 §3.3, owner decision 2026-09-16).
            if (meta.EnvSourced)
            {
                items.Add(new SettingItemDto(
                    Key: meta.Key,
                    Value: EnvSourcedValue(meta.Key),
                    Category: meta.ApiCategory,
                    Label: meta.Label,
                    Description: EnvSourcedDescription(meta.Key),
                    Unit: meta.Unit,
                    ValueType: SystemSettingsCatalog.ValueTypeString,
                    IsEditable: false));
                continue;
            }

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

    private string? EnvSourcedValue(string key) => key switch
    {
        SystemSettingsCatalog.HotWalletAddressKey => _walletAddresses.HotWalletAddress,
        SystemSettingsCatalog.ColdWalletAddressKey => _walletAddresses.ColdWalletAddress,
        _ => null,
    };

    private static string EnvSourcedDescription(string key) => key switch
    {
        SystemSettingsCatalog.HotWalletAddressKey =>
            "Ortam değişkeni HOT_WALLET_ADDRESS. Panelden değiştirilemez; imzalayan servis " +
            "başka bir adrese sweep yapmaz (05 §3.3).",
        SystemSettingsCatalog.ColdWalletAddressKey =>
            "Ortam değişkeni COLD_WALLET_ADDRESS. Panelden değiştirilemez; imzalayan servis " +
            "başka bir adrese soğuk cüzdan transferi yapmaz (05 §3.3).",
        _ => string.Empty,
    };

    public async Task<UpdateSettingOutcome> UpdateAsync(
        string key,
        UpdateSettingRequest request,
        Guid actorAdminId,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || !SystemSettingsCatalog.Contains(key))
            return new UpdateSettingOutcome.NotFound(key);

        // Deployment configuration is not writable from the panel — that is the
        // whole point of moving the platform's own wallet addresses out of the
        // settings table (05 §3.3, owner decision 2026-09-16).
        if (SystemSettingsCatalog.TryGet(key) is { EnvSourced: true })
            return new UpdateSettingOutcome.ReadOnly(key);

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

        // WP14 — propagate the committed change to side-effect targets that live
        // outside this module (Hangfire cron-job re-registration in the API
        // host). Best-effort by contract: the authoritative write above already
        // succeeded, so a propagation failure is logged, not surfaced.
        // setting.Value is non-null here — ValidateSingle rejects a null value.
        await _propagator.PropagateAsync(setting.Key, setting.Value!, cancellationToken);

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
