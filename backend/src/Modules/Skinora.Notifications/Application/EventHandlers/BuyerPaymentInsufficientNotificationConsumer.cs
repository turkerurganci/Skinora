using System.Globalization;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="BuyerPaymentInsufficientEvent"/> (T72 — 02 §4.4
/// "Eksik tutar", 08 §3.4) into a single <see cref="NotificationRequest"/>
/// targeting the buyer. The body tells the buyer the received amount is being
/// refunded to their source address and that they must re-send the correct
/// total before timeout (02 §4.4 "alıcı doğru tutarı baştan gönderir").
/// </summary>
public sealed class BuyerPaymentInsufficientNotificationConsumer
    : NotificationConsumerBase<BuyerPaymentInsufficientEvent>
{
    public BuyerPaymentInsufficientNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<BuyerPaymentInsufficientNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.buyer-payment-insufficient";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        BuyerPaymentInsufficientEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var request = new NotificationRequest
        {
            UserId = domainEvent.BuyerId,
            Type = NotificationType.INSUFFICIENT_PAYMENT,
            TransactionId = domainEvent.TransactionId,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ExpectedAmount"] = domainEvent.ExpectedAmount.ToString("0.######", CultureInfo.InvariantCulture),
                ["ReceivedAmount"] = domainEvent.ReceivedAmount.ToString("0.######", CultureInfo.InvariantCulture),
                ["Stablecoin"] = domainEvent.Stablecoin.ToString(),
                ["SourceAddress"] = domainEvent.SourceAddress,
            },
        };

        IReadOnlyCollection<NotificationRequest> requests = [request];
        return Task.FromResult(requests);
    }
}
