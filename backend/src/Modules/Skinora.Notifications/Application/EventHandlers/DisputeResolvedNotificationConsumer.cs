using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="DisputeResolvedEvent"/> (WP5 / T58 — admin dispute
/// resolution, 03 §6.4) into per-party <see cref="NotificationType.DISPUTE_RESULT"/>
/// notifications. Both the buyer and the seller learn the outcome.
/// </summary>
/// <remarks>
/// Locale coverage for these phrases is forward-deferred to the backend i18n
/// migration (WP17), matching the existing T49/T58 hard-coded Turkish reason
/// strings.
/// </remarks>
public sealed class DisputeResolvedNotificationConsumer
    : NotificationConsumerBase<DisputeResolvedEvent>
{
    public DisputeResolvedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<DisputeResolvedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.dispute-resolved";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        DisputeResolvedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var outcome = domainEvent.Outcome == DisputeResolutionOutcome.BUYER_FAVOR
            ? (domainEvent.BuyerRefunded
                ? "İtirazınız kabul edildi; iadeniz işleme alındı"
                : "İtirazınız kabul edildi")
            : "İtirazınız sonuçlandı; işlem satıcı lehine tamamlandı";

        var requests = new List<NotificationRequest>(2)
        {
            BuildRequest(domainEvent, domainEvent.BuyerId, outcome),
            BuildRequest(domainEvent, domainEvent.SellerId, outcome),
        };

        return Task.FromResult<IReadOnlyCollection<NotificationRequest>>(requests);
    }

    private static NotificationRequest BuildRequest(
        DisputeResolvedEvent domainEvent,
        Guid recipientUserId,
        string outcome) => new()
        {
            UserId = recipientUserId,
            Type = NotificationType.DISPUTE_RESULT,
            TransactionId = domainEvent.TransactionId,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Outcome"] = outcome,
            },
        };
}
