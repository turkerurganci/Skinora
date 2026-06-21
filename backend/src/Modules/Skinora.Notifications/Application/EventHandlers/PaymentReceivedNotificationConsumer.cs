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
/// Translates a <see cref="PaymentReceivedEvent"/> (WP19 — 03 §3.4 step 8) into
/// a <see cref="NotificationType.PAYMENT_RECEIVED"/> for the seller
/// ("payment received").
/// </summary>
/// <remarks>
/// The <c>{Amount}</c> parameter is taken from the event; the recipient
/// (seller) is not on the event, so the consumer does a single read-only
/// <see cref="Transaction"/> lookup for <see cref="Transaction.SellerId"/>. The
/// event already drives a separate realtime consumer; this notification consumer
/// is purely additive (both run via MediatR fan-out).
/// </remarks>
public sealed class PaymentReceivedNotificationConsumer
    : NotificationConsumerBase<PaymentReceivedEvent>
{
    private readonly AppDbContext _dbContext;

    public PaymentReceivedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        AppDbContext dbContext,
        ILogger<PaymentReceivedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
        _dbContext = dbContext;
    }

    protected override string ConsumerName => "notifications.payment-received";

    protected override async Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        PaymentReceivedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var sellerId = await _dbContext.Set<Transaction>()
            .Where(t => t.Id == domainEvent.TransactionId)
            .Select(t => t.SellerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (sellerId == Guid.Empty)
        {
            return [];
        }

        return
        [
            new NotificationRequest
            {
                UserId = sellerId,
                Type = NotificationType.PAYMENT_RECEIVED,
                TransactionId = domainEvent.TransactionId,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Amount"] = domainEvent.Amount.ToString("0.######", CultureInfo.InvariantCulture),
                },
            },
        ];
    }
}
