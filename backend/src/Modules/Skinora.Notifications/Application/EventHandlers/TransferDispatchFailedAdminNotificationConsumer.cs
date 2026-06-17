using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// WP8 — translates a <see cref="TransferDispatchFailedEvent"/> (T73 —
/// 08 §3.3 "tüm denemeler başarısızsa admin'e critical alert") into an
/// <see cref="NotificationType.ADMIN_PAYMENT_FAILURE"/> in-app notification for
/// every admin. The outbound transfer (payout / refund / sweep) exhausted its
/// retries and is parked in FAILED awaiting a manual admin retry.
/// </summary>
public sealed class TransferDispatchFailedAdminNotificationConsumer
    : AdminBroadcastNotificationConsumerBase<TransferDispatchFailedEvent>
{
    public TransferDispatchFailedAdminNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        IAdminRecipientResolver adminRecipients,
        ILogger<TransferDispatchFailedAdminNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, adminRecipients, logger)
    {
    }

    protected override string ConsumerName => "notifications.transfer-dispatch-failed-admin";

    protected override AdminNotificationTemplate BuildAdminTemplate(TransferDispatchFailedEvent domainEvent) =>
        new(
            Type: NotificationType.ADMIN_PAYMENT_FAILURE,
            Parameters: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TransactionId"] = domainEvent.TransactionId.ToString("D"),
                ["ErrorCode"] = $"{domainEvent.Type}:{domainEvent.LastErrorCode ?? "TRANSFER_DISPATCH_FAILED"}",
            },
            TransactionId: domainEvent.TransactionId);
}
