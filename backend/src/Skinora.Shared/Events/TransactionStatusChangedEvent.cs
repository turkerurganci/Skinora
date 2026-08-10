using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Generic transaction state-transition event that drives the RT1
/// <c>TransactionStatusChanged</c> realtime push (WP9 — 07 §11.1, closes
/// T61 K2) for the forward-path legs that raise no dedicated domain event.
/// Producers publish it to the outbox atomically with the transition's
/// <c>SaveChanges</c>, carrying the pre/post status verbatim so the realtime
/// consumer needs no DB lookup.
/// </summary>
/// <remarks>
/// <para>
/// Transitions that already raise a specific domain event keep their own
/// realtime consumer and do NOT publish this event, so no double-push occurs:
/// CREATED → ACCEPTED (<see cref="BuyerAcceptedEvent"/>), the payment
/// confirmation (<see cref="PaymentReceivedEvent"/>), ITEM_DELIVERED →
/// COMPLETED (<see cref="PayoutCompletedEvent"/>), and the cancellation /
/// timeout / dispute / flag / emergency-hold transitions.
/// </para>
/// <para>
/// <b>v3.0 producer state:</b> the legs that used to publish this event were
/// the bot-custody orchestration steps, deleted with that layer in T117. The
/// consumers survive and act on <c>ToStatus</c> ∈ { SELLER_CONFIRMED,
/// PAYMENT_RECEIVED } (03 §3.4 step 1 / §3.5 step 3); the P2P producers for
/// those two legs are written in T123 (<c>seller_confirm_ready</c>) and T124
/// (<c>confirm_payment</c>). Until then no source-side publisher exists — the
/// only publishers are the consumer tests.
/// </para>
/// </remarks>
public record TransactionStatusChangedEvent(
    Guid EventId,
    Guid TransactionId,
    TransactionStatus FromStatus,
    TransactionStatus ToStatus,
    DateTime OccurredAt) : IDomainEvent;
