using Microsoft.Extensions.Logging;
using Skinora.Notifications.Application.Notifications;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Notifications.Application.EventHandlers;

/// <summary>
/// WP7 (F7Gate-OrphanNotificationTypes) — the producer for
/// <see cref="NotificationType.PAYMENT_REFUNDED"/> (06 §2.13: "Alıcı ·
/// İptal/timeout sonrası ödeme iade edildi").
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PaymentRefundToBuyerRequestedEvent"/> had a consumer that queues
/// the on-chain <c>BUYER_REFUND</c> row (WP2) but none that told the buyer. A
/// buyer whose accepted payment was being returned after a cancellation got no
/// word at all — the money moved and the only trace was the transaction page.
/// </para>
/// <para>
/// Distinct from the neighbouring refund notifications, and the distinction is
/// what makes each of them worth sending: <c>LATE_PAYMENT_REFUNDED</c> and
/// <c>OVERPAYMENT_REFUNDED</c> return money the platform never ACCEPTED, while
/// this one returns money it did. The wording a buyer needs differs — one says
/// "your transfer was not usable", the other says "your transaction ended and
/// your payment is on its way back".
/// </para>
/// <para>
/// The refund address is carried so the buyer can see where it went; the amount
/// is deliberately NOT, because the gas fee is deducted at broadcast time
/// (02 §4.6) and quoting a pre-deduction figure here would misstate what
/// arrives.
/// </para>
/// </remarks>
public sealed class PaymentRefundedNotificationConsumer
    : NotificationConsumerBase<PaymentRefundToBuyerRequestedEvent>
{
    public PaymentRefundedNotificationConsumer(
        INotificationDispatcher dispatcher,
        IProcessedEventStore processedEventStore,
        ILogger<PaymentRefundedNotificationConsumer> logger)
        : base(dispatcher, processedEventStore, logger)
    {
    }

    protected override string ConsumerName => "notifications.payment-refunded";

    protected override Task<IReadOnlyCollection<NotificationRequest>> BuildRequestsAsync(
        PaymentRefundToBuyerRequestedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<NotificationRequest> requests =
        [
            new NotificationRequest
            {
                UserId = domainEvent.BuyerId,
                Type = NotificationType.PAYMENT_REFUNDED,
                TransactionId = domainEvent.TransactionId,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["RefundAddress"] = domainEvent.BuyerRefundAddress,
                    ["TransactionId"] = domainEvent.TransactionId.ToString("D"),
                },
            },
        ];

        return Task.FromResult(requests);
    }
}
