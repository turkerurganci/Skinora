namespace Skinora.Platform.Application.Settings;

/// <summary>
/// Read + update for admin-managed platform parameters (07 §9.8–§9.9).
/// Audit log writes use <c>AppDbContext.Set&lt;AuditLog&gt;()</c> directly
/// pending T42's centralised audit pipeline; the call site already supplies
/// <c>ActorId</c> + <c>IpAddress</c> so the eventual refactor only needs to
/// swap the persistence path.
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
}
