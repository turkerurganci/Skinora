using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="WrongTokenRefundRequestedEvent"/> (T72 — 02 §4.4
/// "Yanlış token (desteklenen TRC-20)", 08 §3.4) into a single
/// <see cref="NotificationRequest"/> targeting the buyer. Body text explains
/// the expected vs. actual token and the refund destination.
/// </summary>
public sealed class WrongTokenRefundRequestedNotificationConsumer
    : NotificationConsumerBase<WrongTokenRefundRequestedEvent>
{
    public WrongTokenRefundRequestedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<WrongTokenRefundRequestedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.wrong-token-refund";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        WrongTokenRefundRequestedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var request = new NotificationRequest
        {
            UserId = domainEvent.BuyerId,
            Type = NotificationType.WRONG_TOKEN_REFUND,
            TransactionId = domainEvent.TransactionId,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ExpectedStablecoin"] = domainEvent.ExpectedStablecoin.ToString(),
                ["ActualStablecoin"] = domainEvent.ActualStablecoin.ToString(),
                ["ReceivedAmount"] = MoneyFormat.Display(domainEvent.ReceivedAmount),
                ["SourceAddress"] = domainEvent.SourceAddress,
                // K9 (T147) — where the refund is sent; a pre-T147 event has none and
                // its refund went to the sender.
                ["RefundAddress"] = domainEvent.RefundAddress ?? domainEvent.SourceAddress,
                // T159a — the refund is sent net of its own network fee; a
                // pre-T159a event has neither field and its refund went gross.
                ["RefundAmount"] = MoneyFormat.Display(domainEvent.RefundAmount ?? domainEvent.ReceivedAmount),
                ["GasFee"] = MoneyFormat.Display(domainEvent.GasFee ?? 0m),
            },
        };

        IReadOnlyCollection<NotificationRequest> requests = [request];
        return Task.FromResult(requests);
    }
}
