using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// WP8 — translates a <see cref="DisputeEscalatedEvent"/> (T58 — 03 §6.3/§6.4)
/// into an <see cref="NotificationType.ADMIN_ESCALATION"/> in-app notification
/// for every admin, so an escalated dispute is visible on the admin inbox and
/// not only on the T63 admin queue / dashboard.
/// </summary>
/// <remarks>
/// Independent of <see cref="DisputeEscalatedNotificationConsumer"/> (which
/// notifies the buyer/seller). Both consume the same event under distinct
/// consumer names — MediatR fans out to both and each keeps its own
/// idempotency record. Fires for both manual and WRONG_ITEM auto escalations:
/// either way a dispute now awaits admin review.
/// </remarks>
public sealed class DisputeEscalatedAdminNotificationConsumer
    : AdminBroadcastNotificationConsumerBase<DisputeEscalatedEvent>
{
    public DisputeEscalatedAdminNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        IAdminRecipientResolver adminRecipients,
        ILogger<DisputeEscalatedAdminNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, adminRecipients, logger)
    {
    }

    protected override string ConsumerName => "notifications.dispute-escalated-admin";

    protected override AdminNotificationTemplate BuildAdminTemplate(DisputeEscalatedEvent domainEvent) =>
        new(
            Type: NotificationType.ADMIN_ESCALATION,
            Parameters: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TransactionId"] = domainEvent.TransactionId.ToString("D"),
            },
            TransactionId: domainEvent.TransactionId);
}
