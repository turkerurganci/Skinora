using Microsoft.EntityFrameworkCore;
using Skinora.Notifications.Domain.Entities;
using Skinora.Shared.Models;
using Skinora.Shared.Persistence;

namespace Skinora.Notifications.Application.Inbox;

/// <summary>
/// EF Core-backed read/write model for 07 §8.1–§8.4. The
/// <see cref="Notification"/> table is the source of truth for the
/// platform-in-app channel (05 §7.2): <c>NotificationDispatcher</c>
/// inserts rows during fan-out, this service reads/updates them.
/// </summary>
public sealed class NotificationInboxService : INotificationInboxService
{
    private const int MinPage = 1;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    private readonly AppDbContext _db;

    public NotificationInboxService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<NotificationListItemDto>> ListAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var safePage = page < MinPage ? MinPage : page;
        var safePageSize = pageSize < MinPageSize
            ? DefaultPageSize
            : pageSize > MaxPageSize ? MaxPageSize : pageSize;

        var query = _db.Set<Notification>()
            .AsNoTracking()
            .Where(n => n.UserId == userId);

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(n => new
            {
                n.Id,
                n.Type,
                n.Title,
                n.TransactionId,
                n.IsRead,
                n.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r =>
            {
                var (targetType, targetId) = NotificationTargetMapper.Resolve(r.Type, r.TransactionId);
                return new NotificationListItemDto(
                    Id: r.Id,
                    Type: r.Type.ToString(),
                    Message: r.Title,
                    TargetType: targetType,
                    TargetId: targetId,
                    IsRead: r.IsRead,
                    CreatedAt: r.CreatedAt);
            })
            .ToList();

        return new PagedResult<NotificationListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = safePage,
            PageSize = safePageSize,
        };
    }

    public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken)
        => _db.Set<Notification>()
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsRead)
            .CountAsync(cancellationToken);

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Tracking required so AppDbContext.UpdateAuditFields stamps UpdatedAt
        // — ExecuteUpdateAsync would skip the auditable pipeline.
        var unread = await _db.Set<Notification>()
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(cancellationToken);

        if (unread.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return unread.Count;
    }

    public async Task<MarkReadOutcome> MarkReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        var notification = await _db.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);

        if (notification is null) return MarkReadOutcome.NotFound;
        if (notification.UserId != userId) return MarkReadOutcome.Forbidden;

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return MarkReadOutcome.Success;
    }
}
