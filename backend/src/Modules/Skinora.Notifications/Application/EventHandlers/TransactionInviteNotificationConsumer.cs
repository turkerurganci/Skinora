using System.Globalization;
using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="TransactionCreatedEvent"/> (WP19 — 03 §2.2 step 19,
/// 02 §6.1) into a <see cref="NotificationType.TRANSACTION_INVITE"/> for the
/// buyer.
/// </summary>
/// <remarks>
/// Only a <b>registered</b> buyer is notified: the event carries
/// <see cref="TransactionCreatedEvent.BuyerId"/> only for the STEAM_ID flow when
/// the target buyer already has an account. For OPEN_LINK transactions (or an
/// unregistered STEAM_ID target) <c>BuyerId</c> is <c>null</c> — there is no
/// platform user to target and the seller relays the invite link out-of-band
/// (02 §6.1), so the consumer is a no-op.
/// All template parameters come straight off the event, so no DB re-query is
/// needed.
/// </remarks>
public sealed class TransactionInviteNotificationConsumer
    : NotificationConsumerBase<TransactionCreatedEvent>
{
    public TransactionInviteNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<TransactionInviteNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.transaction-invite";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        TransactionCreatedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<NotificationRequest> requests =
            domainEvent.BuyerId is { } buyerId
                ? [
                    new NotificationRequest
                    {
                        UserId = buyerId,
                        Type = NotificationType.TRANSACTION_INVITE,
                        TransactionId = domainEvent.TransactionId,
                        Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["ItemName"] = domainEvent.ItemName,
                            ["Amount"] = domainEvent.Price.ToString("0.######", CultureInfo.InvariantCulture),
                        },
                    },
                  ]
                : [];

        return Task.FromResult(requests);
    }
}
