using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates an <see cref="AccountUnsuspendedEvent"/> (T105a — 02 §14.0/§16.2)
/// into an <c>ACCOUNT_UNSUSPENDED</c> notification telling the user their
/// account restrictions have been lifted.
/// </summary>
public sealed class AccountUnsuspendedNotificationConsumer
    : NotificationConsumerBase<AccountUnsuspendedEvent>
{
    public AccountUnsuspendedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<AccountUnsuspendedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.account-unsuspended";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        AccountUnsuspendedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var request = new NotificationRequest
        {
            UserId = domainEvent.UserId,
            Type = NotificationType.ACCOUNT_UNSUSPENDED,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal),
        };

        IReadOnlyCollection<NotificationRequest> requests = [request];
        return Task.FromResult(requests);
    }
}
