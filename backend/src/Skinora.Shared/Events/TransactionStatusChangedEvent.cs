using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Generic transaction state-transition event that drives the RT1
/// <c>TransactionStatusChanged</c> realtime push (WP9 — 07 §11.1, closes
/// T61 K2) for the Steam orchestration legs that previously raised no
/// dedicated domain event: <c>SendTradeOfferToSeller</c> (ACCEPTED →
/// TRADE_OFFER_SENT_TO_SELLER), <c>EscrowItem</c> (TRADE_OFFER_SENT_TO_SELLER →
/// ITEM_ESCROWED), <c>SendTradeOfferToBuyer</c> (PAYMENT_RECEIVED →
/// TRADE_OFFER_SENT_TO_BUYER) and <c>DeliverItem</c> (TRADE_OFFER_SENT_TO_BUYER →
/// ITEM_DELIVERED). Producers publish it to the outbox atomically with the
/// transition's <c>SaveChanges</c>, carrying the pre/post status verbatim so
/// the realtime consumer needs no DB lookup.
/// </summary>
/// <remarks>
/// Transitions that already raise a specific domain event keep their own
/// realtime consumer and do NOT publish this event, so no double-push occurs:
/// CREATED → ACCEPTED (<see cref="BuyerAcceptedEvent"/>), ITEM_ESCROWED →
/// PAYMENT_RECEIVED (<see cref="PaymentReceivedEvent"/>), ITEM_DELIVERED →
/// COMPLETED (<see cref="PayoutCompletedEvent"/>), and the cancellation /
/// timeout / dispute / flag / emergency-hold transitions.
/// </remarks>
public record TransactionStatusChangedEvent(
    Guid EventId,
    Guid TransactionId,
    TransactionStatus FromStatus,
    TransactionStatus ToStatus,
    DateTime OccurredAt) : IDomainEvent;
