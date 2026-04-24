using Microsoft.EntityFrameworkCore;
using Skinora.Notifications.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Application.Account;

namespace Skinora.Notifications.Application.Account;

/// <summary>
/// <see cref="INotificationAccountAnonymizer"/> impl — 06 §6.2 notification-side
/// cleanup for account deletion. Placed in <c>Skinora.Notifications</c>
/// because it owns <c>UserNotificationPreference</c> + <c>NotificationDelivery</c>.
/// </summary>
public sealed class NotificationAccountAnonymizer : INotificationAccountAnonymizer
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public NotificationAccountAnonymizer(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<NotificationAnonymizationResult> AnonymizeAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        // Preferences — soft delete live rows and clear ExternalId (PII).
        // IgnoreQueryFilters so already-soft-deleted rows still get their
        // ExternalId scrubbed if the user deletes after a prior channel
        // unlink.
        var preferences = await _db.Set<UserNotificationPreference>()
            .IgnoreQueryFilters()
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var pref in preferences)
        {
            pref.ExternalId = null;
            pref.IsEnabled = false;
            if (!pref.IsDeleted)
            {
                pref.IsDeleted = true;
                pref.DeletedAt = now;
            }
        }

        // Deliveries — mask TargetExternalId. Join via Notification.UserId
        // (NotificationDelivery doesn't hold UserId directly). IgnoreQueryFilters
        // so rows tied to soft-deleted Notifications are still reachable.
        var deliveries = await _db.Set<NotificationDelivery>()
            .IgnoreQueryFilters()
            .Where(d => _db.Set<Notification>()
                .IgnoreQueryFilters()
                .Any(n => n.Id == d.NotificationId && n.UserId == userId))
            .ToListAsync(cancellationToken);

        foreach (var delivery in deliveries)
        {
            delivery.TargetExternalId = Mask(delivery.Channel, delivery.TargetExternalId);
        }

        if (preferences.Count > 0 || deliveries.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return new NotificationAnonymizationResult(
            PreferencesSoftDeleted: preferences.Count,
            DeliveriesMasked: deliveries.Count);
    }

    /// <summary>
    /// 06 §6.2 masking formats. Examples: <c>***@***.com</c> for email,
    /// <c>tg:***{last4}</c> for Telegram, <c>dsc:***{last4}</c> for Discord.
    /// Short inputs fall back to the full-mask literal for the channel so
    /// we never leak fewer-than-4 trailing characters.
    /// </summary>
    private static string Mask(NotificationChannel channel, string targetExternalId)
        => channel switch
        {
            NotificationChannel.EMAIL => "***@***.com",
            NotificationChannel.TELEGRAM => MaskWithTail(targetExternalId, "tg"),
            NotificationChannel.DISCORD => MaskWithTail(targetExternalId, "dsc"),
            _ => "***",
        };

    private static string MaskWithTail(string value, string prefix)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 4)
            return $"{prefix}:***";
        return $"{prefix}:***{value[^4..]}";
    }
}
