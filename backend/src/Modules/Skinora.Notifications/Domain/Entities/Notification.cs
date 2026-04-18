using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Domain.Entities;

/// <summary>
/// Platform in-app notification displayed on user dashboard.
/// All fields per 06 §3.13.
/// </summary>
public class Notification : BaseEntity, ISoftDeletable, IAuditableEntity
{
    // --- Relationships ---
    public Guid UserId { get; set; }
    public Guid? TransactionId { get; set; }

    // --- Notification ---
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
