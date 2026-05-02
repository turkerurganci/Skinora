using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="TransactionTimedOutEvent"/> (T49 — 02 §3.2,
/// 03 §4.1–§4.4) into per-party <see cref="NotificationRequest"/>s. Each
/// recipient (seller, plus the buyer when registered) receives the existing
/// <see cref="NotificationType.TRANSACTION_CANCELLED"/> template populated
/// with phase- and role-specific Turkish reason text taken verbatim from
/// 03 §4.1–§4.4.
/// </summary>
/// <remarks>
/// The reason strings are currently hard-coded in Turkish to keep the
/// notification-template surface flat (one enum, one resx key). Full locale
/// coverage for these phase × role variants is forward-deferred to T97
/// (i18n full coverage) — at that point each variant becomes its own resx
/// key under the existing <c>{Type}_*</c> convention.
/// </remarks>
public sealed class TransactionTimedOutNotificationConsumer
    : NotificationConsumerBase<TransactionTimedOutEvent>
{
    public TransactionTimedOutNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<TransactionTimedOutNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.transaction-timed-out";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        TransactionTimedOutEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var requests = new List<NotificationRequest>(2)
        {
            BuildRequest(
                domainEvent,
                domainEvent.SellerId,
                ResolveReason(domainEvent.Phase, recipientIsSeller: true)),
        };

        if (domainEvent.BuyerId is { } buyerId)
        {
            requests.Add(BuildRequest(
                domainEvent,
                buyerId,
                ResolveReason(domainEvent.Phase, recipientIsSeller: false)));
        }

        return Task.FromResult<IReadOnlyCollection<NotificationRequest>>(requests);
    }

    private static NotificationRequest BuildRequest(
        TransactionTimedOutEvent domainEvent,
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

    // 03 §4.1–§4.4 — per-party reason text. Kept here verbatim so the document
    // is the single source of truth; T97 will move these strings into resx.
    private static string ResolveReason(TimeoutPhase phase, bool recipientIsSeller) =>
        (phase, recipientIsSeller) switch
        {
            (TimeoutPhase.Accept, true) => "Alıcı zamanında kabul etmedi, işlem iptal oldu",
            (TimeoutPhase.Accept, false) => "İşlem zaman aşımı nedeniyle iptal oldu",

            (TimeoutPhase.TradeOfferToSeller, true) => "Zamanında item göndermediniz, işlem iptal oldu",
            (TimeoutPhase.TradeOfferToSeller, false) => "Satıcı item'ı göndermedi, işlem iptal oldu",

            (TimeoutPhase.Payment, true) => "Alıcı ödeme yapmadı, işlem iptal oldu, item'ınız iade edildi",
            (TimeoutPhase.Payment, false) => "Zamanında ödeme yapılmadı, işlem iptal oldu",

            (TimeoutPhase.Delivery, true) => "Alıcı item'ı teslim almadı, item'ınız iade edildi",
            (TimeoutPhase.Delivery, false) => "Zamanında teslim alınmadı, işlem iptal oldu, ödemeniz iade edildi",

            _ => throw new InvalidOperationException(
                $"Unhandled timeout phase {phase} (T49 / 03 §4.1–§4.4)."),
        };
}
