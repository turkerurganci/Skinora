using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="TransactionCancelledEvent"/> (T51 — 02 §7,
/// 03 §2.5 / §3.3, 07 §7.7; extended in T59 — 02 §7, 07 §9.20 / §9.22)
/// into <see cref="NotificationRequest"/>s for the affected parties.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>
///     <c>SELLER</c> / <c>BUYER</c> — the cancelling party already saw the
///     in-app outcome on the response envelope, so they receive no
///     notification. Only the counter-party is notified, mirroring the
///     03 §2.5 / §3.3 flow scripts (T51 originals).
///   </item>
///   <item>
///     <c>ADMIN</c> — neither user initiated the cancel; both seller and
///     buyer (when registered) are notified with the admin's reason text
///     (T59 — 02 §7, 03 §8.8 admin lifecycle).
///   </item>
/// </list>
/// Reuses the existing <see cref="NotificationType.TRANSACTION_CANCELLED"/>
/// template (T37) populated with role-specific Turkish text. Locale coverage
/// for these phrases is forward-deferred to T97 alongside the T49 timeout
/// reason strings.
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

            case CancelledByType.ADMIN:
                // T59 / 02 §7 / 03 §8.8 — neither party initiated the cancel.
                // Both seller and buyer (when registered) get notified with
                // the admin's reason text in the body, mirroring the
                // §2.5 / §3.3 short-form prefix.
                const string AdminReasonPrefix = "İşlem yönetici tarafından iptal edildi";
                requests.Add(BuildRequest(
                    domainEvent,
                    domainEvent.SellerId,
                    AdminReasonPrefix));
                if (domainEvent.BuyerId is { } adminBuyerId)
                {
                    requests.Add(BuildRequest(
                        domainEvent,
                        adminBuyerId,
                        AdminReasonPrefix));
                }
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
