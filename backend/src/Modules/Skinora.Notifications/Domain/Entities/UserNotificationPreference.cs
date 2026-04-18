using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Domain.Entities;

/// <summary>
/// User notification channel preferences and external account links.
/// Filtered unique on (UserId + Channel) and (Channel + ExternalId) among active records.
/// All fields per 06 §3.4.
/// </summary>
public class UserNotificationPreference : BaseEntity, ISoftDeletable, IAuditableEntity
{
    // --- Relationships ---
    public Guid UserId { get; set; }

    // --- Preference ---
    public NotificationChannel Channel { get; set; }
    public bool IsEnabled { get; set; }
    public string? ExternalId { get; set; }
    public DateTime? VerifiedAt { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
