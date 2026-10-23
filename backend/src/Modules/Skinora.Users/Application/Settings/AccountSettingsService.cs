using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Notifications;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Settings;

/// <summary>
/// Builds the <c>GET /users/me/settings</c> snapshot (07 §5.6) by
/// joining <c>User</c> (language, email, email-verified) with the
/// notification preference rows for the three external channels.
/// T165 — every external channel also carries <c>available</c>: false when the
/// deployment switched it off (<c>Provider = disabled</c>, production Discord) or
/// its secrets are incomplete; the UI hides / greys the connect action.
/// </summary>
public sealed class AccountSettingsService : IAccountSettingsService
{
    private readonly AppDbContext _db;
    private readonly INotificationPreferenceStore _preferenceStore;
    private readonly INotificationChannelAvailability _channels;

    public AccountSettingsService(
        AppDbContext db,
        INotificationPreferenceStore preferenceStore,
        INotificationChannelAvailability channels)
    {
        _db = db;
        _preferenceStore = preferenceStore;
        _channels = channels;
    }

    public async Task<AccountSettingsDto?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId && !u.IsDeactivated)
            .Select(u => new { u.PreferredLanguage, u.Email, u.EmailVerifiedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null) return null;

        var prefs = await _preferenceStore.GetAsync(userId, cancellationToken);
        var byChannel = prefs.ToDictionary(p => p.Channel);

        var telegram = byChannel.GetValueOrDefault(NotificationChannel.TELEGRAM);
        var discord = byChannel.GetValueOrDefault(NotificationChannel.DISCORD);
        var email = byChannel.GetValueOrDefault(NotificationChannel.EMAIL);

        return new AccountSettingsDto(
            Language: user.PreferredLanguage,
            Notifications: new NotificationSettingsDto(
                Email: new EmailChannelDto(
                    Enabled: email?.IsEnabled ?? false,
                    Address: user.Email,
                    Verified: user.EmailVerifiedAt.HasValue,
                    Available: _channels.IsAvailable(NotificationChannel.EMAIL)),
                Telegram: new ExternalChannelDto(
                    Enabled: telegram?.IsEnabled ?? false,
                    Connected: telegram is not null,
                    Username: telegram?.ExternalId,
                    Available: _channels.IsAvailable(NotificationChannel.TELEGRAM)),
                Discord: new ExternalChannelDto(
                    Enabled: discord?.IsEnabled ?? false,
                    Connected: discord is not null,
                    Username: discord?.ExternalId,
                    Available: _channels.IsAvailable(NotificationChannel.DISCORD)),
                // 04 §7.6 — platform channel is always on and cannot be disabled.
                Platform: new PlatformChannelDto(Enabled: true, CanDisable: false)));
    }
}
