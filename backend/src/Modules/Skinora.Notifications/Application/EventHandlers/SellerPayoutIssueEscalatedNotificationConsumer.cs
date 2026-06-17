using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// WP8 — translates a <see cref="SellerPayoutIssueEscalatedEvent"/> (T60 —
/// 07 §7.11, 03 §2.4a, 02 §10.3) into an
/// <see cref="NotificationType.ADMIN_PAYMENT_FAILURE"/> in-app notification for
/// the <b>assigned</b> admin only.
/// </summary>
/// <remarks>
/// Unlike the other WP8 admin alerts this is not a broadcast: the event's
/// contract designates a single owning admin
/// (<see cref="SellerPayoutIssueEscalatedEvent.EscalatedToAdminId"/>) who owns
/// the escalation from this point on. The seller is notified separately via the
/// synchronous response payload + transaction detail; the
/// <see cref="SellerPayoutIssueReportedEvent"/> / <c>…ResolvedEvent</c> fan-outs
/// are intentionally deferred per their own contracts.
/// </remarks>
public sealed class SellerPayoutIssueEscalatedNotificationConsumer
    : NotificationConsumerBase<SellerPayoutIssueEscalatedEvent>
{
    public SellerPayoutIssueEscalatedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<SellerPayoutIssueEscalatedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.seller-payout-issue-escalated";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        SellerPayoutIssueEscalatedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var request = new NotificationRequest
        {
            UserId = domainEvent.EscalatedToAdminId,
            Type = NotificationType.ADMIN_PAYMENT_FAILURE,
            TransactionId = domainEvent.TransactionId,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TransactionId"] = domainEvent.TransactionId.ToString("D"),
                ["ErrorCode"] = "PAYOUT_ESCALATED",
            },
        };

        return Task.FromResult<IReadOnlyCollection<NotificationRequest>>(new[] { request });
    }
}
