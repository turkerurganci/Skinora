using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="DisputeAutoResolvedEvent"/> (T58 — 02 §10.1,
/// 03 §6.1 / §6.2) into a single buyer-facing
/// <see cref="NotificationType.DISPUTE_RESULT"/> notification populated
/// with the resolution message provided by the auto-checker (e.g.
/// <c>"Ödemeniz doğrulandı, işlem devam ediyor"</c>).
/// </summary>
public sealed class DisputeAutoResolvedNotificationConsumer
    : NotificationConsumerBase<DisputeAutoResolvedEvent>
{
    public DisputeAutoResolvedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<DisputeAutoResolvedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.dispute-auto-resolved";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        DisputeAutoResolvedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var request = new NotificationRequest
        {
            UserId = domainEvent.BuyerId,
            Type = NotificationType.DISPUTE_RESULT,
            TransactionId = domainEvent.TransactionId,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Outcome"] = domainEvent.Outcome,
            },
        };

        IReadOnlyCollection<NotificationRequest> result = new[] { request };
        return Task.FromResult(result);
    }
}
