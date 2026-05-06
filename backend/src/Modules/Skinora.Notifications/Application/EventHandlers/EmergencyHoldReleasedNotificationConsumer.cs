using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates an <see cref="EmergencyHoldReleasedEvent"/> (T59 — 07 §9.22,
/// 02 §7, 03 §8.8) into <see cref="NotificationRequest"/>s for both parties.
/// </summary>
/// <remarks>
/// The orchestrator only emits this event for the <c>RESUME</c> branch — the
/// <c>CANCEL</c> branch publishes <see cref="TransactionCancelledEvent"/>
/// (with <c>CancelledBy = ADMIN</c>) instead, so the existing T51 consumer
/// reaches both parties with a more accurate "İşlem yönetici tarafından iptal
/// edildi" message. As a defence-in-depth check, this consumer skips
/// <c>Action == CANCEL</c> events to avoid double-notifying the cancel path.
/// </remarks>
public sealed class EmergencyHoldReleasedNotificationConsumer
    : NotificationConsumerBase<EmergencyHoldReleasedEvent>
{
    public EmergencyHoldReleasedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<EmergencyHoldReleasedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.emergency-hold-released";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        EmergencyHoldReleasedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        if (domainEvent.Action != EmergencyHoldReleaseAction.RESUME)
        {
            return Task.FromResult<IReadOnlyCollection<NotificationRequest>>(
                Array.Empty<NotificationRequest>());
        }

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
        EmergencyHoldReleasedEvent domainEvent,
        Guid recipientUserId) => new()
        {
            UserId = recipientUserId,
            Type = NotificationType.EMERGENCY_HOLD_RELEASED,
            TransactionId = domainEvent.TransactionId,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ItemName"] = domainEvent.ItemName,
                ["ResumedStatus"] = domainEvent.ResumedStatus.ToString(),
            },
        };
}
