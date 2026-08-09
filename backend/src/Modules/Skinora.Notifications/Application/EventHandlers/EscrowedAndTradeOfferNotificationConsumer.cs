using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates the generic <see cref="TransactionStatusChangedEvent"/> into the
/// buyer-facing happy-path notifications for the two Steam orchestration legs
/// that ride it (WP19):
/// <list type="bullet">
///   <item><c>ITEM_ESCROWED</c> → <see cref="NotificationType.PAYMENT_WINDOW_OPEN"/>
///   "item reached the platform, payment due" (03 §3.4 step 1).</item>
///   <item><c>TRADE_OFFER_SENT_TO_BUYER</c> →
///   <see cref="NotificationType.DELIVERY_EXPECTED"/> "accept the Steam
///   trade offer to receive your item" (03 §3.5 step 3).</item>
/// </list>
/// </summary>
/// <remarks>
/// One consumer covers both legs because they share the same generic event;
/// every other <c>ToStatus</c> (incl. the SELLER leg) yields no notification.
/// The event carries only the status pair, so the recipient (buyer) and the
/// <c>{Amount}</c>/<c>{PaymentAddress}</c> parameters are read from a single
/// <see cref="Transaction"/> (+ active <c>PaymentAddress</c>) lookup. The event
/// is deliberately not enriched so the WP9 realtime relay it also feeds stays a
/// no-DB pass-through.
/// </remarks>
public sealed class EscrowedAndTradeOfferNotificationConsumer
    : NotificationConsumerBase<TransactionStatusChangedEvent>
{
    private readonly AppDbContext _dbContext;

    public EscrowedAndTradeOfferNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        AppDbContext dbContext,
        ILogger<EscrowedAndTradeOfferNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
        _dbContext = dbContext;
    }

    protected override string ConsumerName => "notifications.transaction-status-changed";

    protected override async Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        TransactionStatusChangedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        if (domainEvent.ToStatus is not (TransactionStatus.SELLER_CONFIRMED
            or TransactionStatus.PAYMENT_RECEIVED))
        {
            return [];
        }

        var data = await _dbContext.Set<Transaction>()
            .Where(t => t.Id == domainEvent.TransactionId)
            .Select(t => new
            {
                t.BuyerId,
                t.SellerId,
                t.TotalAmount,
                ExpectedAmount = t.PaymentAddress != null ? (decimal?)t.PaymentAddress.ExpectedAmount : null,
                PaymentAddress = t.PaymentAddress != null ? t.PaymentAddress.Address : null,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (data?.BuyerId is not { } buyerId)
        {
            return [];
        }

        // v3.0 — this notification changed sides. It used to tell the BUYER to
        // accept a platform-sent trade offer; now it tells the SELLER to send
        // the item directly to the buyer (02 §2.2 step 6).
        if (domainEvent.ToStatus == TransactionStatus.PAYMENT_RECEIVED)
        {
            return
            [
                new NotificationRequest
                {
                    UserId = data.SellerId,
                    Type = NotificationType.DELIVERY_EXPECTED,
                    TransactionId = domainEvent.TransactionId,
                },
            ];
        }

        // ITEM_ESCROWED — buyer must send the expected total to the deposit address.
        var amount = data.ExpectedAmount ?? data.TotalAmount;
        return
        [
            new NotificationRequest
            {
                UserId = buyerId,
                Type = NotificationType.PAYMENT_WINDOW_OPEN,
                TransactionId = domainEvent.TransactionId,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Amount"] = amount.ToString("0.######", CultureInfo.InvariantCulture),
                    ["PaymentAddress"] = data.PaymentAddress ?? string.Empty,
                },
            },
        ];
    }
}
