using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="TransactionCancelledEvent"/> (T51 — 02 §7,
/// 03 §2.5 / §3.3, 07 §7.7) into a single <see cref="NotificationRequest"/>
/// targeted at the counter-party of the cancelling user. The cancelling
/// party already saw the in-app outcome on the response envelope, so they
/// receive no notification — mirroring the 03 §2.5 / §3.3 flow scripts
/// which only notify the OTHER side.
/// </summary>
/// <remarks>
/// Reuses the existing <see cref="NotificationType.TRANSACTION_CANCELLED"/>
/// template (T37) populated with role-specific Turkish text taken from
/// 03 §2.5 step 9 (seller-cancel → buyer) and 03 §3.3 step 8
/// (buyer-cancel → seller). Locale coverage for these phrases is
/// forward-deferred to T97 alongside the T49 timeout reason strings.
/// </remarks>
public sealed class TransactionCancelledNotificationConsumer
    : NotificationConsumerBase<TransactionCancelledEvent>
{
    public TransactionCancelledNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<TransactionCancelledNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.transaction-cancelled";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        TransactionCancelledEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var requests = new List<NotificationRequest>(1);

        switch (domainEvent.CancelledBy)
        {
            case CancelledByType.SELLER:
                // 03 §2.5 step 9 — buyer hears about a seller-driven cancel.
                if (domainEvent.BuyerId is { } buyerId)
                {
                    requests.Add(BuildRequest(
                        domainEvent,
                        buyerId,
                        "İşlem satıcı tarafından iptal edildi"));
                }
                break;

            case CancelledByType.BUYER:
                // 03 §3.3 step 8 — seller hears about a buyer-driven cancel.
                requests.Add(BuildRequest(
                    domainEvent,
                    domainEvent.SellerId,
                    "İşlem alıcı tarafından iptal edildi"));
                break;
        }

        return Task.FromResult<IReadOnlyCollection<NotificationRequest>>(requests);
    }

    private static NotificationRequest BuildRequest(
        TransactionCancelledEvent domainEvent,
        Guid recipientUserId,
        string reason) => new()
        {
            UserId = recipientUserId,
            Type = NotificationType.TRANSACTION_CANCELLED,
            TransactionId = domainEvent.TransactionId,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ItemName"] = domainEvent.ItemName,
                ["Reason"] = reason,
            },
        };
}
