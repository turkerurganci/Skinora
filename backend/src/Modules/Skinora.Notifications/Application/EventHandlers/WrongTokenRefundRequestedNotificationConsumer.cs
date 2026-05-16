using System.Globalization;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
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
                ["ReceivedAmount"] = domainEvent.ReceivedAmount.ToString("0.######", CultureInfo.InvariantCulture),
                ["SourceAddress"] = domainEvent.SourceAddress,
            },
        };

        IReadOnlyCollection<NotificationRequest> requests = [request];
        return Task.FromResult(requests);
    }
}
