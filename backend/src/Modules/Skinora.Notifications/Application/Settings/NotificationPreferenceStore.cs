using Microsoft.EntityFrameworkCore;
using Skinora.Notifications.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Application.Settings;

namespace Skinora.Notifications.Application.Settings;

/// <summary>
/// <see cref="INotificationPreferenceStore"/> implementation that speaks to
/// <c>UserNotificationPreference</c> via <see cref="AppDbContext"/>. Lives
/// in <c>Skinora.Notifications</c> because that module owns the entity; the
/// abstraction is in <c>Skinora.Users</c> so the settings service stays
/// off the Notifications reference graph (T35).
/// </summary>
public sealed class NotificationPreferenceStore : INotificationPreferenceStore
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public NotificationPreferenceStore(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<NotificationPreferenceSnapshot>> GetAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var rows = await _db.Set<UserNotificationPreference>()
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new NotificationPreferenceSnapshot(
                p.Channel, p.IsEnabled, p.ExternalId, p.VerifiedAt))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<NotificationToggleOutcome> ToggleEnabledAsync(
        Guid userId,
        NotificationChannel channel,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var row = await _db.Set<UserNotificationPreference>()
            .FirstOrDefaultAsync(
                p => p.UserId == userId && p.Channel == channel, cancellationToken);

        if (row is null)
            return NotificationToggleOutcome.NotConnected;

        row.IsEnabled = enabled;
        await _db.SaveChangesAsync(cancellationToken);
        return NotificationToggleOutcome.Updated;
    }

    public async Task UpsertPreferenceAsync(
        Guid userId,
        NotificationChannel channel,
        string? externalId,
        bool isEnabled,
        DateTime? verifiedAt,
        CancellationToken cancellationToken)
    {
        var row = await _db.Set<UserNotificationPreference>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                p => p.UserId == userId && p.Channel == channel, cancellationToken);

        if (row is null)
        {
            row = new UserNotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Channel = channel,
                IsEnabled = isEnabled,
                ExternalId = externalId,
                VerifiedAt = verifiedAt,
            };
            _db.Set<UserNotificationPreference>().Add(row);
        }
        else
        {
            row.IsEnabled = isEnabled;
            row.ExternalId = externalId;
            row.VerifiedAt = verifiedAt;
            if (row.IsDeleted)
            {
                row.IsDeleted = false;
                row.DeletedAt = null;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeletePreferenceAsync(
        Guid userId,
        NotificationChannel channel,
        CancellationToken cancellationToken)
    {
        var row = await _db.Set<UserNotificationPreference>()
            .FirstOrDefaultAsync(
                p => p.UserId == userId && p.Channel == channel, cancellationToken);

        if (row is null) return false;

        row.IsDeleted = true;
        row.DeletedAt = _clock.GetUtcNow().UtcDateTime;
        row.IsEnabled = false;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExternalIdInUseByAnotherUserAsync(
        Guid userId,
        NotificationChannel channel,
        string externalId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalId)) return false;

        return await _db.Set<UserNotificationPreference>()
            .AsNoTracking()
            .AnyAsync(
                p => p.UserId != userId &&
                     p.Channel == channel &&
                     p.ExternalId == externalId,
                cancellationToken);
    }
}
