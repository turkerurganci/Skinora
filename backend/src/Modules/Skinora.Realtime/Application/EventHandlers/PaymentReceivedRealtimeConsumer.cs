using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes both <c>PaymentConfirmed</c> and
/// <c>TransactionStatusChanged(ITEM_ESCROWED → PAYMENT_RECEIVED)</c> on
/// <c>/hubs/transactions</c> when blockchain confirmation finalizes the buyer
/// payment (T48 — 07 §11.1, 03 §3.4).
/// </summary>
/// <remarks>
/// <para>
/// The state-machine guard (<c>ConfirmPayment</c> trigger only valid from
/// <c>ITEM_ESCROWED</c>) means the pre-transition state can be hardcoded.
/// </para>
/// <para>
/// 07 §11.1 distinguishes <c>PaymentDetected</c> (mempool / first sighting)
/// from <c>PaymentConfirmed</c> (final block confirmations). The current
/// payments stack only fires <see cref="PaymentReceivedEvent"/> at the
/// confirmation boundary; an explicit <c>PaymentDetected</c> push is
/// forward-deferred to the blockchain monitor task that introduces a
/// dedicated detection event.
/// </para>
/// </remarks>
public sealed class PaymentReceivedRealtimeConsumer
    : RealtimeConsumerBase<PaymentReceivedEvent>
{
    private const int FinalConfirmationCount = 20;

    private readonly ITransactionRealtimePublisher _publisher;

    public PaymentReceivedRealtimeConsumer(
        ITransactionRealtimePublisher publisher,
        IProcessedEventStore processedEventStore,
        ILogger<PaymentReceivedRealtimeConsumer> logger)
        : base(processedEventStore, logger)
    {
        _publisher = publisher;
    }

    protected override string ConsumerName => "realtime.payment-received";

    protected override async Task PublishAsync(
        PaymentReceivedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        await _publisher.PublishPaymentConfirmedAsync(
            new TransactionRealtimePayloads.PaymentConfirmed(
                TransactionId: domainEvent.TransactionId,
                Amount: domainEvent.Amount,
                TxHash: domainEvent.TxHash,
                Confirmations: FinalConfirmationCount),
            cancellationToken);

        await _publisher.PublishStatusChangedAsync(
            new TransactionRealtimePayloads.TransactionStatusChanged(
                TransactionId: domainEvent.TransactionId,
                FromStatus: TransactionStatus.ITEM_ESCROWED,
                ToStatus: TransactionStatus.PAYMENT_RECEIVED,
                Timestamp: domainEvent.OccurredAt),
            cancellationToken);
    }
}
