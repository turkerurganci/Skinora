using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates an <see cref="EmergencyHoldAppliedEvent"/> (T59 — 07 §9.21,
/// 02 §7, 03 §8.8) into <see cref="NotificationRequest"/>s for both parties.
/// </summary>
/// <remarks>
/// Both seller and buyer (when registered) are notified that the transaction
/// has been frozen for review. The admin's reason text is forwarded as the
/// <c>Reason</c> template parameter so support tools can show the operator's
/// rationale alongside the notification.
/// </remarks>
public sealed class EmergencyHoldAppliedNotificationConsumer
    : NotificationConsumerBase<EmergencyHoldAppliedEvent>
{
    public EmergencyHoldAppliedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<EmergencyHoldAppliedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.emergency-hold-applied";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        EmergencyHoldAppliedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var requests = new List<NotificationRequest>(2)
        {
            BuildRequest(domainEvent, domainEvent.SellerId),
        };

        if (domainEvent.BuyerId is { } buyerId)
        {
            requests.Add(BuildRequest(domainEvent, buyerId));
        }

        return Task.FromResult<IReadOnlyCollection<NotificationRequest>>(requests);
    }

    private static NotificationRequest BuildRequest(
        EmergencyHoldAppliedEvent domainEvent,
        Guid recipientUserId) => new()
        {
            UserId = recipientUserId,
            Type = NotificationType.EMERGENCY_HOLD_APPLIED,
            TransactionId = domainEvent.TransactionId,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ItemName"] = domainEvent.ItemName,
                ["Reason"] = domainEvent.Reason,
            },
        };
}
