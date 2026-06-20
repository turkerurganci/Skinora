using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Translates a <see cref="DisputeEscalatedEvent"/> (T58 — 03 §6.3 / §6.4)
/// into per-party <see cref="NotificationRequest"/>s.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>
///     <c>AutoEscalated=true</c> (WRONG_ITEM auto, 03 §6.3 step 5) — both
///     parties receive <see cref="NotificationType.DISPUTE_RESULT"/> with
///     "İşleminiz incelemeye alındı".
///   </item>
///   <item>
///     <c>AutoEscalated=false</c> (manual escalate, 03 §6.4 step 4) — the
///     buyer receives <see cref="NotificationType.DISPUTE_RESULT"/> with
///     "İtirazınız admin ekibine iletildi". Admin queue surfacing is
///     T63's responsibility.
///   </item>
/// </list>
/// WP17: the manual-escalate outcome is localized to the buyer's locale
/// (pre-rendered on <see cref="DisputeEscalatedEvent.OutcomeText"/> by the
/// dispute service). The auto-escalated two-party outcome is not yet localized —
/// each recipient needs its own locale, which the dispatcher applies to the
/// template but not to injected <c>{Outcome}</c> params (notification-architecture
/// follow-up, tracked alongside the T49 timeout reason strings).
/// </remarks>
public sealed class DisputeEscalatedNotificationConsumer
    : NotificationConsumerBase<DisputeEscalatedEvent>
{
    public DisputeEscalatedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<DisputeEscalatedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.dispute-escalated";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        DisputeEscalatedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var requests = new List<NotificationRequest>(2);

        if (domainEvent.AutoEscalated)
        {
            // WP17 — two-party fan-out: each recipient needs its own locale, so
            // per-recipient localization of this {Outcome} fragment stays
            // deferred (notification-architecture follow-up); keep the TR fallback.
            const string AutoOutcome = "İşleminiz incelemeye alındı";
            requests.Add(BuildRequest(domainEvent, domainEvent.BuyerId, AutoOutcome));
            requests.Add(BuildRequest(domainEvent, domainEvent.SellerId, AutoOutcome));
        }
        else
        {
            // WP17 — manual escalate is single-recipient (buyer); DisputeService
            // pre-localizes the outcome in the buyer's locale and rides it on the
            // event. Fall back to Turkish for older events without OutcomeText.
            var manualOutcome = domainEvent.OutcomeText ?? "İtirazınız admin ekibine iletildi";
            requests.Add(BuildRequest(domainEvent, domainEvent.BuyerId, manualOutcome));
        }

        return Task.FromResult<IReadOnlyCollection<NotificationRequest>>(requests);
    }

    private static NotificationRequest BuildRequest(
        DisputeEscalatedEvent domainEvent,
        Guid recipientUserId,
        string outcome) => new()
        {
            UserId = recipientUserId,
            Type = NotificationType.DISPUTE_RESULT,
            TransactionId = domainEvent.TransactionId,
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Outcome"] = outcome,
            },
        };
}
