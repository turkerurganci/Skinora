using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// T129 — the settlement re-check could not be closed by the platform alone and
/// a human has to decide (03 §2.4 step 2, third branch + the ambiguous
/// departure). Raised at most once per transaction
/// (<c>Transaction.SettlementEscalatedAt</c> is the idempotency marker); the
/// payout stays parked either way.
/// </summary>
/// <remarks>
/// Three situations reach here, and none of them may move money on their own:
/// <list type="bullet">
///   <item>an inventory that stayed unreadable past
///         <c>settlement.unreadable_escalation_hours</c> — absence of
///         information, never a finding (08 §2.3);</item>
///   <item>the item left the buyer but nothing shows it went back to the seller
///         — a reversal and an onward sale look identical from one side, and
///         they call for opposite actions;</item>
///   <item>a reversal signature raised while the auto-refund launch gate is
///         closed.</item>
/// </list>
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction awaiting an admin decision.</param>
/// <param name="SellerId">Seller side of the transaction.</param>
/// <param name="BuyerId">Buyer side of the transaction.</param>
/// <param name="Reason">Machine-readable reason code (see <c>SettlementReviewReasons</c>).</param>
/// <param name="Detail">The check's own summary of what it saw.</param>
/// <param name="OccurredAt">UTC timestamp the escalation was committed.</param>
public record SettlementReviewRequiredEvent(
    Guid EventId,
    Guid TransactionId,
    Guid SellerId,
    Guid? BuyerId,
    string Reason,
    string Detail,
    DateTime OccurredAt) : IDomainEvent;

/// <summary>
/// Reason codes for <see cref="SettlementReviewRequiredEvent.Reason"/>.
/// </summary>
public static class SettlementReviewReasons
{
    /// <summary>An inventory stayed unreadable past the escalation threshold.</summary>
    public const string Unreadable = "SETTLEMENT_UNREADABLE";

    /// <summary>The item left the buyer without proof it returned to the seller.</summary>
    public const string AmbiguousDeparture = "SETTLEMENT_AMBIGUOUS_DEPARTURE";

    /// <summary>Reversal signature raised while the auto-refund gate is closed.</summary>
    public const string ReversalGated = "SETTLEMENT_REVERSAL_GATED";
}
