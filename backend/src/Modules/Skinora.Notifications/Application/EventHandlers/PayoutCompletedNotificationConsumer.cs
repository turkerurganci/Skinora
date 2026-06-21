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
/// Translates a <see cref="PayoutCompletedEvent"/> (WP19 — 03 §2.4 step 5/6,
/// 06 §2.13) into the completion notifications:
/// <list type="bullet">
///   <item>Seller — <see cref="NotificationType.SELLER_PAYMENT_SENT"/>
///   "your payout was sent" (03 §2.4 step 5).</item>
///   <item>Seller — <see cref="NotificationType.TRANSACTION_COMPLETED"/>
///   "transaction completed" (03 §2.4 step 6).</item>
///   <item>Buyer — <see cref="NotificationType.TRANSACTION_COMPLETED"/>
///   (06 §2.13 "her ikisi"; the buyer's completion is deferred to COMPLETED per
///   03 §3.5 step 10, never sent at ITEM_DELIVERED).</item>
/// </list>
/// </summary>
/// <remarks>
/// The payout event fires only on on-chain finality, which is also what drives
/// the WP1 completion consumer's <c>Complete</c> trigger — so both completion
/// notices are emitted in the same outbox batch as the COMPLETED transition.
/// The <c>{Amount}</c> for the payout notice is the event's net amount; the
/// recipients and <c>{ItemName}</c> are read from a single read-only
/// <see cref="Transaction"/> lookup. This consumer lives in the Notifications
/// assembly and is independent of the Transactions-assembly
/// <c>PayoutCompletedConsumer</c> that fires the state-machine transition; both
/// consume the event via MediatR fan-out.
/// </remarks>
public sealed class PayoutCompletedNotificationConsumer
    : NotificationConsumerBase<PayoutCompletedEvent>
{
    private readonly AppDbContext _dbContext;

    public PayoutCompletedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        AppDbContext dbContext,
        ILogger<PayoutCompletedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
        _dbContext = dbContext;
    }

    protected override string ConsumerName => "notifications.payout-completed";

    protected override async Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        PayoutCompletedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var data = await _dbContext.Set<Transaction>()
            .Where(t => t.Id == domainEvent.TransactionId)
            .Select(t => new { t.SellerId, t.BuyerId, t.ItemName })
            .FirstOrDefaultAsync(cancellationToken);

        if (data is null || data.SellerId == Guid.Empty)
        {
            return [];
        }

        var requests = new List<NotificationRequest>(3)
        {
            // 03 §2.4 step 5 — "Ödemeniz gönderildi".
            new()
            {
                UserId = data.SellerId,
                Type = NotificationType.SELLER_PAYMENT_SENT,
                TransactionId = domainEvent.TransactionId,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Amount"] = domainEvent.NetAmount.ToString("0.######", CultureInfo.InvariantCulture),
                },
            },
            // 03 §2.4 step 6 / 06 §2.13 — completion notice to the seller.
            new()
            {
                UserId = data.SellerId,
                Type = NotificationType.TRANSACTION_COMPLETED,
                TransactionId = domainEvent.TransactionId,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ItemName"] = data.ItemName,
                },
            },
        };

        // 06 §2.13 "her ikisi" — completion notice to the buyer (when registered).
        if (data.BuyerId is { } buyerId)
        {
            requests.Add(new NotificationRequest
            {
                UserId = buyerId,
                Type = NotificationType.TRANSACTION_COMPLETED,
                TransactionId = domainEvent.TransactionId,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ItemName"] = data.ItemName,
                },
            });
        }

        return requests;
    }
}
