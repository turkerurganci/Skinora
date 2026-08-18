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
    /// The timeout proceeds: the caller runs the shared cancellation path
    /// (03 §4.4 steps 2–7), which refunds the buyer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two producers reach this value, and they disagree about fault — which is
    /// why the sentence "timeout recorded against the seller" is NOT written
    /// here any more (T131 validation finding N1):
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Proven non-delivery.</b> The platform can prove the seller still
    ///     holds the item (<see cref="DeliveryVerdict.NoMovement"/> or a
    ///     decidable <see cref="DeliveryVerdict.Inconclusive"/>). The timeout is
    ///     the seller's, and 06 §3.1 charges it to them.
    ///   </item>
    ///   <item>
    ///     <b>Released by an admin ruling</b> (T131). A misdelivery signature
    ///     was held — the item LEFT the seller and never arrived, the opposite
    ///     of the case above — and an admin has since ruled on the dispute, so
    ///     the cancellation is no longer the silent one 02 §9.2 forbids. Here
    ///     the seller was cleared, so the round stamps
    ///     <c>Transaction.TimeoutReleasedByAdminRulingAt</c> and the reputation
    ///     and cooldown maps leave the row out entirely (03 §6.4).
    ///   </item>
    /// </list>
    /// <para>
    /// The caller's work is identical either way; only what the row means to the
    /// seller's record differs, and that is carried on the transaction rather
    /// than on this enum.
    /// </para>
    /// </remarks>
    Cancel,

    /// <summary>
    /// Nothing may be concluded: the launch gate holds an inventory-based
    /// delivery for human review, the misdelivery signature went to an admin,
    /// or the platform could not see enough to decide. The transaction stays in
    /// <c>PAYMENT_RECEIVED</c> and is neither delivered nor cancelled.
    /// </summary>
    Held,
}
