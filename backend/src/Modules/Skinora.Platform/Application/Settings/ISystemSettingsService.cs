namespace Skinora.Platform.Application.Settings;

/// <summary>
/// Read + update for admin-managed platform parameters (07 §9.8–§9.9).
/// Audit log rows are emitted through <see cref="Audit.IAuditLogger"/>
/// (T42, 09 §18.6) so the actor invariant (06 §8.6a) and the future audit
/// query layer share a single write path.
/// </summary>
public interface ISystemSettingsService
{
    /// <summary>AD8 — list every catalog setting with its current value.</summary>
    Task<SettingsListResponse> ListAsync(CancellationToken cancellationToken);

    /// <summary>AD9 — update a single setting and emit a SYSTEM_SETTING_CHANGED audit row.</summary>
    Task<UpdateSettingOutcome> UpdateAsync(
        string key,
        UpdateSettingRequest request,
        Guid actorAdminId,
        string? ipAddress,
        CancellationToken cancellationToken);
}

/// <summary>Outcome of <see cref="ISystemSettingsService.UpdateAsync"/>.</summary>
public abstract record UpdateSettingOutcome
{
    public sealed record Success(UpdateSettingResponse Response) : UpdateSettingOutcome;

    public sealed record NotFound(string Key) : UpdateSettingOutcome;

    public sealed record ValidationFailed(string Message) : UpdateSettingOutcome;

    /// <summary>
    /// The key exists but its value is deployment configuration, not an
    /// admin-editable row (05 §3.3, owner decision 2026-09-16) — the panel
    /// shows it and the update endpoint refuses it.
    /// </summary>
    public sealed record ReadOnly(string Key) : UpdateSettingOutcome;
}
