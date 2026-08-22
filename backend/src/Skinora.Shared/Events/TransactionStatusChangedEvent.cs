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
/// CREATED → ACCEPTED (<see cref="BuyerAcceptedEvent"/>), ITEM_DELIVERED →
/// COMPLETED (<see cref="PayoutCompletedEvent"/>), and the cancellation /
/// timeout / dispute / flag / emergency-hold transitions.
/// </para>
/// <para>
/// <b>Two transitions deliberately publish this event ALONGSIDE their own
/// domain event</b>, and in both the second event exists because no consumer
/// of the first one does the job:
/// <list type="bullet">
///   <item><b>The payment confirmation (T140).</b> It publishes BOTH this
///   event and <see cref="PaymentReceivedEvent"/>, because the two carry
///   different facts to disjoint consumers: PaymentReceived carries the money
///   (amount / token / txHash) and drives the <c>PaymentConfirmed</c> push
///   plus the seller's <c>PAYMENT_RECEIVED</c> notification, while this event
///   carries the status pair and is the ONLY producer of the seller's
///   <c>DELIVERY_EXPECTED</c> notification — the single prompt for the one
///   action P2P waits on (03 §3.5 step 2).</item>
///   <item><b>The settlement reversal</b> (<c>ITEM_DELIVERED → REFUNDED</c>,
///   <see cref="SettlementReversalDetectedEvent"/>, T129). Its own event has
///   only a notification/fraud consumer, so this event is what stops the
///   parties' screens showing a settlement countdown for a transaction that
///   has refunded.</item>
/// </list>
/// Double-push is avoided in both by ownership, not by suppression: each
/// sibling event's consumers push their own payload and this event's relay
/// owns <c>StatusChanged</c>, so the relay stays a verbatim pass-through with
/// no per-status special cases.
/// </para>
/// <para>
/// This paragraph is a correction, and the correction is the point: until T140
/// the list above named the payment confirmation among the transitions that
/// must NOT publish this event. That rule was written for the custody era,
/// when the notification riding this leg (<c>TRADE_OFFER_SENT_TO_BUYER</c>)
/// came from the bot dispatch job and this event carried a realtime badge and
/// nothing else. v3.0 moved the notification onto this event and flipped its
/// recipient to the seller, but the stale rule survived — so T124 wired
/// SELLER_CONFIRMED and left PAYMENT_RECEIVED unwired, exactly as instructed,
/// and the consumer leg sat as unreachable dead code until the end-to-end
/// suite measured its absence (T138).
/// </para>
/// <para>
/// <b>v3.0 producer state:</b> the legs that used to publish this event were
/// the bot-custody orchestration steps, deleted with that layer in T117. The
/// consumers survive and act on <c>ToStatus</c> ∈ { SELLER_CONFIRMED,
/// PAYMENT_RECEIVED } (03 §3.4 step 1 / §3.5 step 2). Both P2P producers now
/// exist: <c>TransactionReadinessService</c> (<c>seller_confirm_ready</c>,
/// T123) and <c>AmountValidationService.AdvanceStateMachineAsync</c>
/// (<c>confirm_payment</c>, T140).
/// </para>
/// </remarks>
public record TransactionStatusChangedEvent(
    Guid EventId,
    Guid TransactionId,
    TransactionStatus FromStatus,
    TransactionStatus ToStatus,
    DateTime OccurredAt) : IDomainEvent;
