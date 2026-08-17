namespace Skinora.Transactions.Application.Settlement;

/// <summary>
/// Live reader for the 02 §4.5.1 settlement parameters.
/// </summary>
public interface ISettlementSettingsProvider
{
    Task<SettlementSettings> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Snapshot of the settlement window parameters (T129).
/// </summary>
/// <param name="SettlementDays">
/// How long a delivered transaction waits before the seller can be paid
/// (<c>payout_settlement_days</c>). Written into
/// <c>Transaction.PayoutEligibleAt</c> on entry to ITEM_DELIVERED.
/// </param>
/// <param name="UnreadableEscalationHours">
/// How long the end-of-window re-check may stay inconclusive because an
/// inventory could not be read before the transaction is escalated to an admin
/// (<c>settlement.unreadable_escalation_hours</c>, 03 §2.4 step 2 third branch).
/// </param>
/// <param name="ReversalAutoRefundEnabled">
/// The launch gate (<c>settlement.reversal_auto_refund_enabled</c>). While
/// false, a reversal signature is recorded and escalated but does not move
/// money on its own.
/// </param>
public sealed record SettlementSettings(
    int SettlementDays,
    int UnreadableEscalationHours,
    bool ReversalAutoRefundEnabled);
