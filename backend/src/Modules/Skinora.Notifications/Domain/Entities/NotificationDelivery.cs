using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Notifications.Domain.Entities;

/// <summary>
/// External channel delivery record — tracks each delivery attempt per notification per channel.
/// Workflow record: no soft delete, status+attempt updates only. Terminal states (SENT/FAILED) are frozen.
/// All fields per 06 §3.13a.
/// </summary>
public class NotificationDelivery : BaseEntity, IAuditableEntity
{
    // --- Relationships ---
    public Guid NotificationId { get; set; }

    // --- Delivery ---
    public NotificationChannel Channel { get; set; }
    public string TargetExternalId { get; set; } = string.Empty;
    public DeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }
}
