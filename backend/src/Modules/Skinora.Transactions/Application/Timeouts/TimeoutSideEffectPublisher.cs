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
                FromStatus: previousStatus,
                OccurredAt: occurredAt),
            cancellationToken);

        switch (phase)
        {
            case TimeoutPhase.Payment:
                // 03 §4.3 — no item refund exists in the P2P model (the item
                // never left the seller). Only the late-payment watch remains.
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
                // 03 §4.4 — the seller failed to deliver, so the buyer's money
                // goes back. There is no item to return: it never left the
                // seller's inventory.
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
                    // BuyerId / BuyerRefundAddress should never be null at
                    // PAYMENT_RECEIVED (they are required for BuyerAccept per
                    // 06 §3.5), but log defensively so a schema regression
                    // doesn't silently swallow a refund.
                    _logger.LogError(
                        "Payment refund event skipped for transaction {TransactionId}: BuyerId or BuyerRefundAddress missing in PAYMENT_RECEIVED.",
                        transaction.Id);
                }
                break;

            case TimeoutPhase.Accept:
            case TimeoutPhase.SellerConfirm:
                // 03 §4.1 / §4.2 — no money has moved yet. Nothing to refund.
                break;
        }
    }

    private static TimeoutPhase MapPhase(TransactionStatus previousStatus) => previousStatus switch
    {
        TransactionStatus.CREATED => TimeoutPhase.Accept,
        TransactionStatus.ACCEPTED => TimeoutPhase.SellerConfirm,
        TransactionStatus.SELLER_CONFIRMED => TimeoutPhase.Payment,
        TransactionStatus.PAYMENT_RECEIVED => TimeoutPhase.Delivery,
        _ => throw new InvalidOperationException(
            $"Timeout side effects are only defined for CREATED / ACCEPTED / SELLER_CONFIRMED / PAYMENT_RECEIVED (got {previousStatus})."),
    };
}
