using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="DeliveryDetectedEvent"/> (P2P-DeliveryPollingJob,
/// 02 §9.2) into a <c>DELIVERY_DETECTED</c> notification for the BUYER: the
/// item seems to have arrived — check the Steam inventory and, if it is there,
/// confirm receipt.
/// </summary>
/// <remarks>
/// The seller is deliberately not told. The observation is the platform's
/// inference behind a closed launch gate (DEPLOY_RUNBOOK §H); telling the
/// seller "delivered" would promise a payout the inference cannot release.
/// The buyer's own confirmation is what releases it.
/// </remarks>
public sealed class DeliveryDetectedNotificationConsumer
    : NotificationConsumerBase<DeliveryDetectedEvent>
{
    public DeliveryDetectedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<DeliveryDetectedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.delivery-detected";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        DeliveryDetectedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<NotificationRequest> requests =
        [
            new NotificationRequest
            {
                UserId = domainEvent.BuyerId,
                Type = NotificationType.DELIVERY_DETECTED,
                TransactionId = domainEvent.TransactionId,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ItemName"] = domainEvent.ItemName,
                },
            },
        ];
        return Task.FromResult(requests);
    }
}
