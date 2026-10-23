using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Notifications;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// T129 — puts a parked settlement on every admin's inbox
/// (<see cref="NotificationType.ADMIN_ESCALATION"/>).
/// </summary>
/// <remarks>
/// The transaction behind this notification is holding a buyer's money with no
/// automatic way forward: the platform either could not read an inventory, or
/// read one and could not tell a reversal from an onward sale, or found a
/// reversal while the auto-refund gate was closed. All three wait for a person,
/// which is why they are announced rather than logged (03 §2.4 step 2).
/// </remarks>
public sealed class SettlementReviewAdminNotificationConsumer
    : AdminBroadcastNotificationConsumerBase<SettlementReviewRequiredEvent>
{
    public SettlementReviewAdminNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        IAdminRecipientResolver adminRecipients,
        IAdminMoneyAlarmObserver moneyAlarms,
        ILogger<SettlementReviewAdminNotificationConsumer> logger)
        // T165 — money alarm (IAdminMoneyAlarmObserver): also reaches the
        // operator's Telegram chat / e-mail through Grafana.
        : base(dispatcher, processedEventStore, adminRecipients, moneyAlarms, logger)
    {
    }

    protected override string ConsumerName => "notifications.settlement-review-admin";

    protected override AdminNotificationTemplate BuildAdminTemplate(
        SettlementReviewRequiredEvent domainEvent) =>
        new(
            Type: NotificationType.ADMIN_ESCALATION,
            Parameters: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TransactionId"] = domainEvent.TransactionId.ToString("D"),
            },
            TransactionId: domainEvent.TransactionId);
}
