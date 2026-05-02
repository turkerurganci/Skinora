using System.Globalization;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="TimeoutWarningEvent"/> (T48 — 02 §3.4, 05 §4.4)
/// into a single <see cref="NotificationRequest"/> targeting the recipient
/// carried on the event (the buyer for the ITEM_ESCROWED payment stage). The
/// dispatcher then writes the platform-in-app row plus one
/// <c>NotificationDelivery</c> per enabled external channel.
/// </summary>
public sealed class TimeoutWarningNotificationConsumer
    : NotificationConsumerBase<TimeoutWarningEvent>
{
    public TimeoutWarningNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<TimeoutWarningNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.timeout-warning";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        TimeoutWarningEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var request = new NotificationRequest
        {
            UserId = domainEvent.RecipientUserId,
            Type = NotificationType.TIMEOUT_WARNING,
            TransactionId = domainEvent.TransactionId,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ItemName"] = domainEvent.ItemName,
                ["RemainingMinutes"] = domainEvent.RemainingMinutes
                    .ToString(CultureInfo.InvariantCulture),
            },
        };

        IReadOnlyCollection<NotificationRequest> requests = [request];
        return Task.FromResult(requests);
    }
}
