using System.Globalization;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="BuyerPaymentExcessRefundedEvent"/> (T72 — 02 §4.4
/// "Fazla tutar" / multi-payment, 08 §3.4) into a single
/// <see cref="NotificationRequest"/> targeting the buyer. Body text reports
/// the refund amount and the destination source address.
/// </summary>
public sealed class BuyerPaymentExcessRefundedNotificationConsumer
    : NotificationConsumerBase<BuyerPaymentExcessRefundedEvent>
{
    public BuyerPaymentExcessRefundedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<BuyerPaymentExcessRefundedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.buyer-payment-excess-refunded";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        BuyerPaymentExcessRefundedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var request = new NotificationRequest
        {
            UserId = domainEvent.BuyerId,
            Type = NotificationType.OVERPAYMENT_REFUNDED,
            TransactionId = domainEvent.TransactionId,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ExpectedAmount"] = domainEvent.ExpectedAmount.ToString("0.######", CultureInfo.InvariantCulture),
                ["ReceivedAmount"] = domainEvent.ReceivedAmount.ToString("0.######", CultureInfo.InvariantCulture),
                ["ExcessAmount"] = domainEvent.ExcessAmount.ToString("0.######", CultureInfo.InvariantCulture),
                ["Stablecoin"] = domainEvent.Stablecoin.ToString(),
                ["SourceAddress"] = domainEvent.SourceAddress,
            },
        };

        IReadOnlyCollection<NotificationRequest> requests = [request];
        return Task.FromResult(requests);
    }
}
