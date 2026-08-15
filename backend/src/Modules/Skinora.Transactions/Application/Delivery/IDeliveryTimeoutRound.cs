using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// T127 — the delivery-verification round 05 §4.4 and 03 §4.4 require BEFORE a
/// delivery timeout is allowed to cancel anything.
/// </summary>
/// <remarks>
/// <para>
/// The delivery phase is the one timeout where expiry alone is not a finding.
/// The seller may well have sent the item and simply never been confirmed —
/// cancelling then refunds the buyer and records the failure against a seller
/// who delivered (02 §9.2). So the deadline opens a decision rather than
/// closing one, and this is where that decision is made.
/// </para>
/// <para>
/// <b>Unit of work.</b> Writes land on the caller's tracked
/// <c>AppDbContext</c>; nothing here calls <c>SaveChangesAsync</c>. The scanner
/// owns one transaction per batch so a delivery conclusion, its evidence
/// capture and any cancellation in the same pass commit together.
/// </para>
/// </remarks>
public interface IDeliveryTimeoutRound
{
    /// <summary>
    /// Run one round for an overdue <c>PAYMENT_RECEIVED</c> transaction.
    /// </summary>
    Task<DeliveryTimeoutDecision> RunAsync(
        Transaction transaction,
        CancellationToken cancellationToken);
}

/// <summary>
/// What the caller must do next. The three values are exhaustive over the five
/// <see cref="DeliveryVerdict"/> outcomes.
/// </summary>
public enum DeliveryTimeoutDecision
{
    /// <summary>
    /// Delivery was proven. The round already fired <c>DeliverItem</c>, wrote
    /// the history row and published the status change — the caller does
    /// nothing but save.
    /// </summary>
    Delivered,

    /// <summary>
    /// The platform can PROVE the seller still holds the item, so the timeout
    /// proceeds: the caller runs the shared cancellation path (03 §4.4 steps
    /// 2–7 — refund to the buyer, timeout recorded against the seller).
    /// </summary>
    Cancel,

    /// <summary>
    /// Nothing may be concluded: the launch gate holds an inventory-based
    /// delivery for human review, the misdelivery signature went to an admin,
    /// or the platform could not see enough to decide. The transaction stays in
    /// <c>PAYMENT_RECEIVED</c> and is neither delivered nor cancelled.
    /// </summary>
    Held,
}
