namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Orchestrates the <c>POST /transactions/:id/accept</c> happy path
/// (T46 — 07 §7.6, 03 §3.2). Resolves the buyer (Yöntem 1 Steam ID match
/// or Yöntem 2 first-comer wins for OPEN_LINK), runs the refund-wallet
/// pipeline (TRC-20 format → sanctions → cooldown), drives the state
/// machine <c>CREATED → ACCEPTED</c> transition, snapshots
/// <c>BuyerRefundAddress</c>, and publishes <c>BuyerAcceptedEvent</c>
/// atomically with SaveChanges.
/// <para>
/// T119a adds the two v3.0 gates 07 §7.6 requires between the wallet pipeline
/// and the transition: the mandatory <c>steamTradeUrl</c> is parsed, checked to
/// belong to the caller's own Steam account and snapshotted (normalized) onto
/// <c>Transaction.BuyerTradeUrl</c>, and the buyer's Mobile Authenticator is
/// verified live through <c>GetTradeHoldDurations</c> (02 §9.1) — fail-closed
/// when Steam cannot be reached.
/// </para>
/// </summary>
public interface ITransactionAcceptanceService
{
    Task<AcceptTransactionOutcome> AcceptAsync(
        Guid buyerId,
        Guid transactionId,
        AcceptTransactionRequest request,
        CancellationToken cancellationToken);
}
