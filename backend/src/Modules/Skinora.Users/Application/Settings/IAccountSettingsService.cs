namespace Skinora.Users.Application.Settings;

/// <summary>
/// Reads the composite settings snapshot (language + notification channels)
/// used by <c>GET /users/me/settings</c> (07 §5.6).
/// </summary>
public interface IAccountSettingsService
{
    Task<AccountSettingsDto?> GetAsync(Guid userId, CancellationToken cancellationToken);
}
