using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// WP7 (F7Gate-OrphanNotificationTypes) — the producer for
/// <see cref="NotificationType.TRANSACTION_FLAGGED"/> (06 §2.13: "Satıcı ·
/// İşlem incelemeye alındı").
/// </summary>
/// <remarks>
/// <para>
/// The catalogue has promised this notification since T54 and nothing ever
/// sent it: <see cref="FraudFlagCreatedEvent"/> fed only the admin broadcast
/// (<see cref="FraudFlagCreatedAdminNotificationConsumer"/>). A seller whose
/// transaction was held for review learned about it by opening the page and
/// noticing the status had changed.
/// </para>
/// <para>
/// Scoped to TRANSACTION-level flags — the ones with a
/// <see cref="FraudFlagCreatedEvent.TransactionId"/>. An account-level flag has
/// none, and telling the user "your transaction is under review" for an
/// account-scope signal would name a transaction that does not exist. Those
/// flags reach the user through the suspension path instead (02 §14.0).
/// </para>
/// <para>
/// <c>UserId</c> is the flagged party, which for a pre-create flag is the
/// seller: <c>FraudPreCheckService</c> evaluates price deviation, high volume
/// and dormancy against the seller (02 §14.4).
/// </para>
/// </remarks>
public sealed class TransactionFlaggedNotificationConsumer
    : NotificationConsumerBase<FraudFlagCreatedEvent>
{
    public TransactionFlaggedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<TransactionFlaggedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.transaction-flagged";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        FraudFlagCreatedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        // Gated on SCOPE, not merely on TransactionId being set: the scope is
        // what the flag MEANS, and reading it directly says so. The null check
        // rides along because TRANSACTION_PRE_CREATE always carries one.
        if (domainEvent.Scope != FraudFlagScope.TRANSACTION_PRE_CREATE
            || domainEvent.TransactionId is null)
        {
            IReadOnlyCollection<NotificationRequest> none = [];
            return Task.FromResult(none);
        }

        var request = new NotificationRequest
        {
            UserId = domainEvent.UserId,
            Type = NotificationType.TRANSACTION_FLAGGED,
            TransactionId = domainEvent.TransactionId,
            // The template renders {TransactionId}; the resolver substitutes
            // only what Parameters carries, so an unset key would reach the
            // user as the literal placeholder.
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TransactionId"] = domainEvent.TransactionId.Value.ToString("D"),
            },
        };

        IReadOnlyCollection<NotificationRequest> requests = [request];
        return Task.FromResult(requests);
    }
}
