using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="BuyerAcceptedEvent"/> (WP19 — 03 §3.2 step 7, 07 §8.1)
/// into a <see cref="NotificationType.BUYER_ACCEPTED"/> for the seller
/// ("buyer accepted — send your item").
/// </summary>
/// <remarks>
/// The recipient (seller) is read straight off the event. The
/// <c>{BuyerName}</c> template parameter requires the buyer's display name, so
/// the consumer does a single read-only <see cref="User"/> lookup via the
/// shared <see cref="AppDbContext"/> — the same in-assembly re-query pattern
/// <see cref="NotificationDispatcher"/> already uses for locale resolution. The
/// event is intentionally not enriched with the name to avoid rippling into the
/// other (realtime / side-effect) consumers of <c>BuyerAcceptedEvent</c>.
/// </remarks>
public sealed class BuyerAcceptedNotificationConsumer
    : NotificationConsumerBase<BuyerAcceptedEvent>
{
    private const string UnknownBuyerName = "Buyer";

    private readonly AppDbContext _dbContext;

    public BuyerAcceptedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        AppDbContext dbContext,
        ILogger<BuyerAcceptedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
        _dbContext = dbContext;
    }

    protected override string ConsumerName => "notifications.buyer-accepted";

    protected override async Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        BuyerAcceptedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var buyerName = await _dbContext.Set<User>()
            .Where(u => u.Id == domainEvent.BuyerId)
            .Select(u => u.SteamDisplayName)
            .FirstOrDefaultAsync(cancellationToken);

        return
        [
            new NotificationRequest
            {
                UserId = domainEvent.SellerId,
                Type = NotificationType.BUYER_ACCEPTED,
                TransactionId = domainEvent.TransactionId,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["BuyerName"] = string.IsNullOrWhiteSpace(buyerName) ? UnknownBuyerName : buyerName,
                },
            },
        ];
    }
}
