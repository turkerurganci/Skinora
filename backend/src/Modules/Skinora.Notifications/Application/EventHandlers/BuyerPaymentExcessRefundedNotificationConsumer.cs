using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Domain;
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
                ["ExpectedAmount"] = MoneyFormat.Display(domainEvent.ExpectedAmount),
                ["ReceivedAmount"] = MoneyFormat.Display(domainEvent.ReceivedAmount),
                ["ExcessAmount"] = MoneyFormat.Display(domainEvent.ExcessAmount),
                ["Stablecoin"] = domainEvent.Stablecoin.ToString(),
                ["SourceAddress"] = domainEvent.SourceAddress,
                // K9 (T147) — where the refund is sent; a pre-T147 event has none and
                // its refund went to the sender.
                ["RefundAddress"] = domainEvent.RefundAddress ?? domainEvent.SourceAddress,
                // T159a — the refund is sent net of its own network fee; a
                // pre-T159a event has neither field and its refund went gross.
                ["RefundAmount"] = MoneyFormat.Display(domainEvent.RefundAmount ?? domainEvent.ExcessAmount),
                ["GasFee"] = MoneyFormat.Display(domainEvent.GasFee ?? 0m),
            },
        };

        IReadOnlyCollection<NotificationRequest> requests = [request];
        return Task.FromResult(requests);
    }
}
