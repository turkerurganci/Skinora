using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Users.Application.Settings;

public sealed class NotificationPreferenceService : INotificationPreferenceService
{
    private readonly AppDbContext _db;
    private readonly INotificationPreferenceStore _store;

    public NotificationPreferenceService(AppDbContext db, INotificationPreferenceStore store)
    {
        _db = db;
        _store = store;
    }

    public async Task<NotificationPreferenceUpdateResult> UpdateAsync(
        Guid userId,
        UpdateNotificationsRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(
                u => u.Id == userId && !u.IsDeactivated, cancellationToken);

        if (user is null)
            return NotificationPreferenceUpdateResult.Failure(
                NotificationPreferenceUpdateStatus.UserNotFound);

        // Email is special: the address is stored on the User row and changing
        // it invalidates the prior verification. The enabled flag lives on a
        // preference row that the store creates on first toggle when the user
        // has already set an email address (no "connect" flow for email — the
        // address itself is the link).
        if (request.Email is not null)
        {
            var address = request.Email.Address?.Trim();
            if (!string.IsNullOrEmpty(address))
            {
                if (!IsValidEmail(address))
                    return NotificationPreferenceUpdateResult.Failure(
                        NotificationPreferenceUpdateStatus.ValidationError, "email");

                if (!string.Equals(user.Email, address, StringComparison.OrdinalIgnoreCase))
                {
                    user.Email = address;
                    user.EmailVerifiedAt = null;
                }
            }

            if (request.Email.Enabled.HasValue)
            {
                if (string.IsNullOrEmpty(user.Email))
                    return NotificationPreferenceUpdateResult.Failure(
                        NotificationPreferenceUpdateStatus.ChannelNotConnected, "email");

                var outcome = await _store.ToggleEnabledAsync(
                    userId,
                    NotificationChannel.EMAIL,
                    request.Email.Enabled.Value,
                    cancellationToken);

                if (outcome == NotificationToggleOutcome.NotConnected)
                {
                    await _store.UpsertPreferenceAsync(
                        userId,
                        NotificationChannel.EMAIL,
                        externalId: user.Email,
                        isEnabled: request.Email.Enabled.Value,
                        verifiedAt: user.EmailVerifiedAt,
                        cancellationToken);
                }
            }
        }

        if (request.Telegram?.Enabled is bool telegramEnabled)
        {
            var outcome = await _store.ToggleEnabledAsync(
                userId, NotificationChannel.TELEGRAM, telegramEnabled, cancellationToken);

            if (outcome == NotificationToggleOutcome.NotConnected)
                return NotificationPreferenceUpdateResult.Failure(
                    NotificationPreferenceUpdateStatus.ChannelNotConnected, "telegram");
        }

        if (request.Discord?.Enabled is bool discordEnabled)
        {
            var outcome = await _store.ToggleEnabledAsync(
                userId, NotificationChannel.DISCORD, discordEnabled, cancellationToken);

            if (outcome == NotificationToggleOutcome.NotConnected)
                return NotificationPreferenceUpdateResult.Failure(
                    NotificationPreferenceUpdateStatus.ChannelNotConnected, "discord");
        }

        await _db.SaveChangesAsync(cancellationToken);
        return NotificationPreferenceUpdateResult.Success();
    }

    private static bool IsValidEmail(string address)
    {
        try
        {
            _ = new MailAddress(address);
            return address.Contains('@', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
