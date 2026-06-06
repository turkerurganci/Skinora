using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates an <see cref="AccountSuspendedEvent"/> (T105a — 02 §14.0/§16.2)
/// into an <c>ACCOUNT_SUSPENDED</c> notification for the affected user. The
/// admin's reason text is forwarded as the <c>Reason</c> template parameter.
/// </summary>
public sealed class AccountSuspendedNotificationConsumer
    : NotificationConsumerBase<AccountSuspendedEvent>
{
    public AccountSuspendedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<AccountSuspendedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.account-suspended";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        AccountSuspendedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var request = new NotificationRequest
        {
            UserId = domainEvent.UserId,
            Type = NotificationType.ACCOUNT_SUSPENDED,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Reason"] = domainEvent.Reason,
            },
        };

        IReadOnlyCollection<NotificationRequest> requests = [request];
        return Task.FromResult(requests);
    }
}
