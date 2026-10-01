using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="SellerPayoutDeferredEvent"/> (02 §4.7 —
/// PayoutStallsOnNonPositiveNet) into an
/// <see cref="NotificationType.ADMIN_PAYMENT_FAILURE"/> in-app notification for
/// every admin. A seller payout whose runtime gas estimate consumed the whole
/// price has been deferred three times; the job keeps retrying, and the usual
/// remedy is restoring the hot wallet's payout Energy (DEPLOY_RUNBOOK §C.2).
/// </summary>
public sealed class SellerPayoutDeferredAdminNotificationConsumer
    : AdminBroadcastNotificationConsumerBase<SellerPayoutDeferredEvent>
{
    /// <summary>Error code the admin sees in the ADMIN_PAYMENT_FAILURE body.</summary>
    public const string ErrorCode = "SELLER_PAYOUT_DEFERRED";

    public SellerPayoutDeferredAdminNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        IAdminRecipientResolver adminRecipients,
        ILogger<SellerPayoutDeferredAdminNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, adminRecipients, logger)
    {
    }

    protected override string ConsumerName => "notifications.seller-payout-deferred-admin";

    protected override AdminNotificationTemplate BuildAdminTemplate(SellerPayoutDeferredEvent domainEvent) =>
        new(
            Type: NotificationType.ADMIN_PAYMENT_FAILURE,
            Parameters: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TransactionId"] = domainEvent.TransactionId.ToString("D"),
                ["ErrorCode"] = ErrorCode,
            },
            TransactionId: domainEvent.TransactionId);
}
