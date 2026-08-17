using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// T129 — tells both parties that the settlement check found the trade reversed
/// (02 §4.5.1: "Her iki tarafa bildirim gider", 03 §2.4 step 2).
/// </summary>
/// <remarks>
/// Reuses <see cref="NotificationType.TRANSACTION_CANCELLED"/> rather than
/// introducing a type of its own: the template already carries the
/// (item, reason) shape this message needs, and the two parties need opposite
/// reason texts — the buyer is told their money is coming back, the seller is
/// told why no payment was made. A new type would have bought a different label
/// on the same envelope at the cost of four locale files.
/// </remarks>
public sealed class SettlementReversalNotificationConsumer
    : NotificationConsumerBase<SettlementReversalDetectedEvent>
{
    public SettlementReversalNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<SettlementReversalNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.settlement-reversal";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        SettlementReversalDetectedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var requests = new List<NotificationRequest>(2)
        {
            Build(domainEvent, domainEvent.SellerId,
                "Mutabakat kontrolünde trade'in geri alındığı tespit edildi — ödeme yapılmadı, "
                + "para alıcıya iade edildi"),
        };

        // Guid.Empty is the publisher's "no buyer" sentinel; a delivered
        // transaction always has one, so this is defensive rather than expected.
        if (domainEvent.BuyerId != Guid.Empty)
        {
            requests.Add(Build(domainEvent, domainEvent.BuyerId,
                "Trade geri alındığı için işlem iade edildi — ödemeniz iade adresinize gönderiliyor"));
        }

        return Task.FromResult<IReadOnlyCollection<NotificationRequest>>(requests);
    }

    private static NotificationRequest Build(
        SettlementReversalDetectedEvent domainEvent,
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
