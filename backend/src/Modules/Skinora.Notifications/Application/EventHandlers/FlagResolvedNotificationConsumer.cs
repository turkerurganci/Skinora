using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// WP7 (F7Gate-OrphanNotificationTypes) — the producer for
/// <see cref="NotificationType.FLAG_RESOLVED"/> (06 §2.13: "Satıcı · Flag
/// sonuçlandı (onay veya red)").
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="TransactionFlaggedNotificationConsumer"/>, and
/// the half that matters more: without it a seller told "your transaction is
/// under review" was never told the review had ENDED. Both outcomes are
/// reported — an approved flag cancels the transaction, a rejected one lets it
/// continue, and either way the wait is over.
/// </para>
/// <para>
/// Scoped to TRANSACTION-level flags for the same reason as its sibling: an
/// account-level resolution is an account matter and reaches the user through
/// the suspension / unsuspension notifications instead.
/// </para>
/// <para>
/// <c>Outcome</c> is carried as a parameter rather than split into two
/// notification types, because the catalogue defines ONE type covering both
/// ("onay veya red"). The renderer decides the wording.
/// </para>
/// </remarks>
public sealed class FlagResolvedNotificationConsumer
    : NotificationConsumerBase<FraudFlagApprovedEvent>
{
    public FlagResolvedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<FlagResolvedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.flag-resolved-approved";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        FraudFlagApprovedEvent domainEvent,
        CancellationToken cancellationToken)
        => Task.FromResult(BuildFlagResolved(
            domainEvent.Scope, domainEvent.UserId, domainEvent.TransactionId, "APPROVED"));

    internal static IReadOnlyCollection<NotificationRequest> BuildFlagResolved(
        FraudFlagScope scope,
        Guid userId,
        Guid? transactionId,
        string outcome)
    {
        if (scope != FraudFlagScope.TRANSACTION_PRE_CREATE || transactionId is null) return [];

        return
        [
            new NotificationRequest
            {
                UserId = userId,
                Type = NotificationType.FLAG_RESOLVED,
                TransactionId = transactionId,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Outcome"] = outcome,
                    ["TransactionId"] = transactionId.Value.ToString("D"),
                },
            },
        ];
    }
}

/// <summary>
/// The rejected-flag half of <see cref="NotificationType.FLAG_RESOLVED"/>.
/// Separate class because the outbox dispatches per event type; the request
/// shape is shared so the two outcomes cannot drift apart.
/// </summary>
public sealed class FlagRejectedNotificationConsumer
    : NotificationConsumerBase<FraudFlagRejectedEvent>
{
    public FlagRejectedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<FlagRejectedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.flag-resolved-rejected";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        FraudFlagRejectedEvent domainEvent,
        CancellationToken cancellationToken)
        => Task.FromResult(
            FlagResolvedNotificationConsumer.BuildFlagResolved(
                domainEvent.Scope, domainEvent.UserId, domainEvent.TransactionId, "REJECTED"));
}
