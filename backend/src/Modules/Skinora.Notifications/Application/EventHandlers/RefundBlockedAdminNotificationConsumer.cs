using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// WP8 — translates a <see cref="RefundBlockedAdminAlertEvent"/> (T53 —
/// 09 §14.4 "iade &lt; minimum: iade yapılmaz → admin alert") into an
/// <see cref="NotificationType.ADMIN_PAYMENT_FAILURE"/> in-app notification for
/// every admin. This is the admin-inbox fan-out the event's contract names: a
/// suppressed refund leaves residue an operator must dispose of.
/// </summary>
public sealed class RefundBlockedAdminNotificationConsumer
    : AdminBroadcastNotificationConsumerBase<RefundBlockedAdminAlertEvent>
{
    public RefundBlockedAdminNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        IAdminRecipientResolver adminRecipients,
        ILogger<RefundBlockedAdminNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, adminRecipients, logger)
    {
    }

    protected override string ConsumerName => "notifications.refund-blocked-admin";

    protected override AdminNotificationTemplate BuildAdminTemplate(RefundBlockedAdminAlertEvent domainEvent) =>
        new(
            Type: NotificationType.ADMIN_PAYMENT_FAILURE,
            Parameters: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TransactionId"] = domainEvent.TransactionId.ToString("D"),
                ["ErrorCode"] = $"REFUND_BLOCKED:{domainEvent.Reason}",
            },
            TransactionId: domainEvent.TransactionId);
}
