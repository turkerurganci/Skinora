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
///   <item><c>ITEM_ESCROWED</c> → <see cref="NotificationType.ITEM_ESCROWED"/>
///   "item reached the platform, payment due" (03 §3.4 step 1).</item>
///   <item><c>TRADE_OFFER_SENT_TO_BUYER</c> →
///   <see cref="NotificationType.TRADE_OFFER_SENT_TO_BUYER"/> "accept the Steam
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
        if (domainEvent.ToStatus is not (TransactionStatus.ITEM_ESCROWED
            or TransactionStatus.TRADE_OFFER_SENT_TO_BUYER))
        {
            return [];
        }

        var data = await _dbContext.Set<Transaction>()
            .Where(t => t.Id == domainEvent.TransactionId)
            .Select(t => new
            {
                t.BuyerId,
                t.TotalAmount,
                ExpectedAmount = t.PaymentAddress != null ? (decimal?)t.PaymentAddress.ExpectedAmount : null,
                PaymentAddress = t.PaymentAddress != null ? t.PaymentAddress.Address : null,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (data?.BuyerId is not { } buyerId)
        {
            return [];
        }

        if (domainEvent.ToStatus == TransactionStatus.TRADE_OFFER_SENT_TO_BUYER)
        {
            return
            [
                new NotificationRequest
                {
                    UserId = buyerId,
                    Type = NotificationType.TRADE_OFFER_SENT_TO_BUYER,
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
                Type = NotificationType.ITEM_ESCROWED,
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
