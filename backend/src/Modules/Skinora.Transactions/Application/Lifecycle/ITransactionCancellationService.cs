namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// User-initiated cancellation pipeline (T51 — 07 §7.7, 02 §7,
/// 03 §2.5 / §3.3). Handles seller and buyer cancel requests; admin-initiated
/// cancellation (T59) and timeout-initiated cancellation (T49) live in their
/// own services because the side-effect surface differs.
/// </summary>
public interface ITransactionCancellationService
{
    /// <summary>
    /// Cancel <paramref name="transactionId"/> on behalf of
    /// <paramref name="callerUserId"/> with the supplied reason. The caller
    /// must be either the seller or buyer; the service derives the role from
    /// the transaction record. Post-payment states return
    /// <c>PAYMENT_ALREADY_SENT</c>; pre-payment states fire the appropriate
    /// state-machine trigger and emit the corresponding outbox events
    /// (refund, notification, reputation/cooldown updates).
    /// </summary>
    Task<CancelTransactionOutcome> CancelAsync(
        Guid callerUserId,
        Guid transactionId,
        CancelTransactionRequest request,
        CancellationToken cancellationToken);
}
