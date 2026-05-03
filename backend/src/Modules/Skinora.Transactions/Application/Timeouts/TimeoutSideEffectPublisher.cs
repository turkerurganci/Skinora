using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Timeouts;

/// <summary>
/// Default <see cref="ITimeoutSideEffectPublisher"/> — emits the
/// <see cref="TransactionTimedOutEvent"/> notification fan-out trigger plus
/// the phase-specific refund / late-payment-monitor events per 02 §3.2 and
/// 03 §4.1–§4.4.
/// </summary>
public sealed class TimeoutSideEffectPublisher : ITimeoutSideEffectPublisher
{
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<TimeoutSideEffectPublisher> _logger;

    public TimeoutSideEffectPublisher(
        IOutboxService outbox,
        TimeProvider clock,
        ILogger<TimeoutSideEffectPublisher> logger)
    {
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task PublishAsync(
        Transaction transaction,
        TransactionStatus previousStatus,
        CancellationToken cancellationToken = default)
    {
        var phase = MapPhase(previousStatus);
        var occurredAt = _clock.GetUtcNow().UtcDateTime;

        await _outbox.PublishAsync(
            new TransactionTimedOutEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                Phase: phase,
                SellerId: transaction.SellerId,
                BuyerId: transaction.BuyerId,
                ItemName: transaction.ItemName,
                OccurredAt: occurredAt),
            cancellationToken);

        switch (phase)
        {
            case TimeoutPhase.Payment:
                // 03 §4.3 — item back to seller + keep watching for late payment.
                await _outbox.PublishAsync(
                    new ItemRefundToSellerRequestedEvent(
                        EventId: Guid.NewGuid(),
                        TransactionId: transaction.Id,
                        SellerId: transaction.SellerId,
                        Trigger: ItemRefundTrigger.TimeoutPayment,
                        OccurredAt: occurredAt),
                    cancellationToken);

                if (transaction.BuyerId is { } buyerIdForMonitor
                    && !string.IsNullOrWhiteSpace(transaction.BuyerRefundAddress))
                {
                    await _outbox.PublishAsync(
                        new LatePaymentMonitorRequestedEvent(
                            EventId: Guid.NewGuid(),
                            TransactionId: transaction.Id,
                            BuyerId: buyerIdForMonitor,
                            BuyerRefundAddress: transaction.BuyerRefundAddress!,
                            OccurredAt: occurredAt),
                        cancellationToken);
                }
                else
                {
                    _logger.LogWarning(
                        "Late payment monitor skipped for transaction {TransactionId}: BuyerId or BuyerRefundAddress missing.",
                        transaction.Id);
                }
                break;

            case TimeoutPhase.Delivery:
                // 03 §4.4 — item back to seller + payment back to buyer.
                await _outbox.PublishAsync(
                    new ItemRefundToSellerRequestedEvent(
                        EventId: Guid.NewGuid(),
                        TransactionId: transaction.Id,
                        SellerId: transaction.SellerId,
                        Trigger: ItemRefundTrigger.TimeoutDelivery,
                        OccurredAt: occurredAt),
                    cancellationToken);

                if (transaction.BuyerId is { } buyerIdForRefund
                    && !string.IsNullOrWhiteSpace(transaction.BuyerRefundAddress))
                {
                    await _outbox.PublishAsync(
                        new PaymentRefundToBuyerRequestedEvent(
                            EventId: Guid.NewGuid(),
                            TransactionId: transaction.Id,
                            BuyerId: buyerIdForRefund,
                            BuyerRefundAddress: transaction.BuyerRefundAddress!,
                            OccurredAt: occurredAt),
                        cancellationToken);
                }
                else
                {
                    // BuyerId / BuyerRefundAddress should never be null at TRADE_OFFER_SENT_TO_BUYER
                    // (they are required for BuyerAccept per 06 §3.5), but log defensively so a
                    // schema regression doesn't silently swallow a refund.
                    _logger.LogError(
                        "Payment refund event skipped for transaction {TransactionId}: BuyerId or BuyerRefundAddress missing in TRADE_OFFER_SENT_TO_BUYER.",
                        transaction.Id);
                }
                break;

            case TimeoutPhase.Accept:
            case TimeoutPhase.TradeOfferToSeller:
                // 03 §4.1 / §4.2 — item never reached the platform. No refund needed.
                break;
        }
    }

    private static TimeoutPhase MapPhase(TransactionStatus previousStatus) => previousStatus switch
    {
        TransactionStatus.CREATED => TimeoutPhase.Accept,
        TransactionStatus.TRADE_OFFER_SENT_TO_SELLER => TimeoutPhase.TradeOfferToSeller,
        TransactionStatus.ITEM_ESCROWED => TimeoutPhase.Payment,
        TransactionStatus.TRADE_OFFER_SENT_TO_BUYER => TimeoutPhase.Delivery,
        _ => throw new InvalidOperationException(
            $"Timeout side effects are only defined for CREATED / TRADE_OFFER_SENT_TO_SELLER / ITEM_ESCROWED / TRADE_OFFER_SENT_TO_BUYER (got {previousStatus})."),
    };
}
