using System.Globalization;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="LatePaymentRefundRequestedEvent"/> (T75 — 02 §4.4 /
/// 03 §5.4 "Gecikmeli ödeme") into a single <see cref="NotificationRequest"/>
/// targeting the buyer. The body confirms the late transfer that landed at the
/// already-cancelled transaction's deposit address was refunded to the buyer's
/// source wallet (gas fee deducted at T73 broadcast).
/// </summary>
/// <remarks>
/// Completes the §5.4 notification leg that the sibling payment-edge-case
/// consumers (insufficient / excess / wrong-token) already cover. The
/// <see cref="NotificationType.LATE_PAYMENT_REFUNDED"/> enum value, its
/// <c>NotificationTemplates.*.resx</c> entries and the <c>EmailCategoryMap</c>
/// mapping shipped earlier, but no consumer translated the event into a
/// notification until T110 — so the refund was dispatched silently.
/// </remarks>
public sealed class LatePaymentRefundRequestedNotificationConsumer
    : NotificationConsumerBase<LatePaymentRefundRequestedEvent>
{
    public LatePaymentRefundRequestedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<LatePaymentRefundRequestedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.late-payment-refund";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        LatePaymentRefundRequestedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var request = new NotificationRequest
        {
            UserId = domainEvent.BuyerId,
            Type = NotificationType.LATE_PAYMENT_REFUNDED,
            TransactionId = domainEvent.TransactionId,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // LATE_PAYMENT_REFUNDED_Body template references {Amount}; the
                // refund row carries the full received amount (gas deducted at
                // broadcast, 02 §4.6).
                ["Amount"] = domainEvent.ReceivedAmount.ToString("0.######", CultureInfo.InvariantCulture),
                ["Stablecoin"] = domainEvent.Stablecoin.ToString(),
                ["SourceAddress"] = domainEvent.SourceAddress,
            },
        };

        IReadOnlyCollection<NotificationRequest> requests = [request];
        return Task.FromResult(requests);
    }
}
