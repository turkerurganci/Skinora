using Microsoft.Extensions.Logging;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Events;
using Skinora.Shared.Outbox;

namespace Skinora.Realtime.Application.EventHandlers;

/// <summary>
/// Pushes <c>PaymentConfirmed</c> on <c>/hubs/transactions</c> when blockchain
/// confirmation finalizes the buyer payment (T48 — 07 §11.1, 03 §3.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>It does NOT push <c>TransactionStatusChanged</c> (T140).</b> It used to,
/// with the pre-transition state hardcoded to <c>SELLER_CONFIRMED</c> — the
/// state-machine guard on the <c>ConfirmPayment</c> trigger made that safe, but
/// it was a stand-in for a producer that did not exist. Since T140 the payment
/// confirmation publishes <see cref="TransactionStatusChangedEvent"/> itself
/// (it has to: that event is the only producer of the seller's
/// <c>DELIVERY_EXPECTED</c> notification), so
/// <c>TransactionStatusChangedRealtimeConsumer</c> now relays the status pair
/// verbatim from the producer. Keeping the push here as well would deliver the
/// identical payload twice; the hardcode is removed rather than the relay
/// filtered, so this leg matches the SELLER_CONFIRMED one (T123) and the relay
/// stays free of per-status special cases.
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

    protected override Task PublishAsync(
        PaymentReceivedEvent domainEvent,
        CancellationToken cancellationToken) =>
        _publisher.PublishPaymentConfirmedAsync(
            new TransactionRealtimePayloads.PaymentConfirmed(
                TransactionId: domainEvent.TransactionId,
                Amount: domainEvent.Amount,
                TxHash: domainEvent.TxHash,
                Confirmations: FinalConfirmationCount),
            cancellationToken);
}
