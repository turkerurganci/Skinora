using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// Backlog <c>F7Gate-EventsWithoutConsumer</c> (second half) — the consumer for
/// <see cref="SellerPayoutIssueResolvedEvent"/>, and the producer for
/// <see cref="NotificationType.PAYOUT_ISSUE_RESOLVED"/>.
/// </summary>
/// <remarks>
/// <para>
/// The gap this closes was real and one-sided: a seller who reported "my payout
/// never arrived" was told the report had been received, and then never told
/// anything again. The event was published on both resolution paths and nobody
/// consumed it, so the person waiting on the answer was the only one who never
/// got it.
/// </para>
/// <para>
/// <b>Why a new notification type.</b> The nearest existing type,
/// <see cref="NotificationType.SELLER_PAYMENT_SENT"/>, renders "{Amount} USDT
/// was sent to your wallet". This event carries no amount — so the template
/// would show a literal <c>{Amount}</c> to the user (05 §7.3 substitutes
/// missing keys verbatim rather than throwing) — and on the admin path there is
/// no observed transfer at all, only an operator's decision. Reusing it would
/// have made the platform assert a payment it had not seen. The new type states
/// only what is true on both paths: the reported problem is closed.
/// </para>
/// <para>
/// <b>Both paths share one notification, deliberately.</b>
/// <see cref="SellerPayoutIssueResolvedEvent.PayoutTxHash"/> (chain-confirmed)
/// and <see cref="SellerPayoutIssueResolvedEvent.ResolvedByAdminId"/> (admin)
/// discriminate the paths, but the seller's question — "is my payout sorted?" —
/// has the same answer either way, and the transaction page carries the detail.
/// Splitting into two types would ask the reader to care about our internal
/// resolution route.
/// </para>
/// <para>
/// The template takes no placeholders, so <c>Parameters</c> is left empty on
/// purpose: WP7 shipped two templates that asked for values their producer did
/// not have, and the cheapest defence is a template with nothing to fill.
/// <c>TransactionId</c> is still set on the request — that is the inbox target
/// link (07 §8.1), not a template parameter.
/// </para>
/// </remarks>
public sealed class PayoutIssueResolvedNotificationConsumer
    : NotificationConsumerBase<SellerPayoutIssueResolvedEvent>
{
    public PayoutIssueResolvedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<PayoutIssueResolvedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.payout-issue-resolved";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        SellerPayoutIssueResolvedEvent domainEvent,
        CancellationToken cancellationToken)
        => Task.FromResult(Build(domainEvent));

    internal static IReadOnlyCollection<NotificationRequest> Build(
        SellerPayoutIssueResolvedEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return
        [
            new NotificationRequest
            {
                UserId = domainEvent.SellerId,
                Type = NotificationType.PAYOUT_ISSUE_RESOLVED,
                TransactionId = domainEvent.TransactionId,
            },
        ];
    }
}
