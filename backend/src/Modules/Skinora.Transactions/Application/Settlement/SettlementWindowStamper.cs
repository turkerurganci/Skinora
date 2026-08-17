using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Settlement;

/// <summary>
/// T129 — the single writer of <see cref="Transaction.PayoutEligibleAt"/>
/// (02 §4.5.1, 06 §3.5).
/// </summary>
/// <remarks>
/// <para>
/// Called by every path that fires <c>DeliverItem</c>, <b>before</b> the
/// trigger: the ITEM_DELIVERED guard refuses the transition without the column,
/// so a caller that forgets this cannot deliver at all. That ordering is
/// deliberate. Twice in this phase a task opened a producer and left its
/// consumer ungated (T124's delivery window, T126's payout queue), and both
/// times the miss was invisible because nothing structurally required the two
/// to travel together. Here they do.
/// </para>
/// <para>
/// The anchor is the caller's delivery instant rather than
/// <see cref="Transaction.ItemDeliveredAt"/>, which the state machine stamps on
/// entry a few microseconds later from its own <c>DateTime.UtcNow</c>. 06 §3.5
/// defines the column as <c>ItemDeliveredAt + settlement window</c> and that
/// holds to within the duration of the transition itself; anchoring here is
/// what lets the guard see the value at all.
/// </para>
/// </remarks>
public static class SettlementWindowStamper
{
    /// <summary>
    /// Open the settlement window for a transaction about to enter
    /// ITEM_DELIVERED. Idempotent: an already-stamped column is left alone so a
    /// retried round cannot push a seller's payout date further out.
    /// </summary>
    public static void Stamp(Transaction transaction, DateTime deliveredAtUtc, int settlementDays)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (transaction.PayoutEligibleAt.HasValue) return;

        transaction.PayoutEligibleAt = deliveredAtUtc.AddDays(settlementDays);
    }
}
