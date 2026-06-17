using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// WP8 — translates a <see cref="FraudFlagCreatedEvent"/> (T54 — 02 §14.0,
/// 03 §7, 07 §9.2) into an <see cref="NotificationType.ADMIN_FLAG_ALERT"/>
/// in-app notification for every admin. This is the admin-alert channel the
/// event's contract names: a new flag lands on the admin review queue with a
/// flag-queue inbox target keyed by <see cref="Notification.FlagId"/>.
/// </summary>
/// <remarks>
/// Party-side notifications (seller/buyer) are not produced here — at creation
/// the parties only see the FLAGGED transaction status (07 §7.4); the
/// approve/reject events own their notifications. Account-level flags have no
/// transaction, so the transaction target is omitted while the flag target is
/// always present.
/// </remarks>
public sealed class FraudFlagCreatedAdminNotificationConsumer
    : AdminBroadcastNotificationConsumerBase<FraudFlagCreatedEvent>
{
    public FraudFlagCreatedAdminNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        IAdminRecipientResolver adminRecipients,
        ILogger<FraudFlagCreatedAdminNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, adminRecipients, logger)
    {
    }

    protected override string ConsumerName => "notifications.fraud-flag-created-admin";

    protected override AdminNotificationTemplate BuildAdminTemplate(FraudFlagCreatedEvent domainEvent)
    {
        var transactionLabel = domainEvent.TransactionId?.ToString("D") ?? "(account-level)";

        return new AdminNotificationTemplate(
            Type: NotificationType.ADMIN_FLAG_ALERT,
            Parameters: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TransactionId"] = transactionLabel,
                ["Reason"] = domainEvent.Type.ToString(),
            },
            TransactionId: domainEvent.TransactionId,
            FlagId: domainEvent.FraudFlagId);
    }
}
