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

    /// <summary>
    /// WP8 — links an admin flag-alert (<c>ADMIN_FLAG_ALERT</c>) notification to
    /// the <c>FraudFlag</c> it was raised for, so the admin inbox renders a
    /// flag-queue target (07 §8.1). NULL for every non-flag notification.
    /// Indexed (filtered, soft-delete table); no FK — <c>FraudFlag</c> lives in
    /// the Fraud module and is soft-deleted, so the link is held logically by id
    /// rather than a cross-module referential constraint.
    /// </summary>
    public Guid? FlagId { get; set; }

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
