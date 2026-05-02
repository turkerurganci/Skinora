namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Orchestrates the <c>POST /transactions/:id/accept</c> happy path
/// (T46 — 07 §7.6, 03 §3.2). Resolves the buyer (Yöntem 1 Steam ID match
/// or Yöntem 2 first-comer wins for OPEN_LINK), runs the refund-wallet
/// pipeline (TRC-20 format → sanctions → cooldown), drives the state
/// machine <c>CREATED → ACCEPTED</c> transition, snapshots
/// <c>BuyerRefundAddress</c> + <c>RefundAddressChangedAt</c>, and publishes
/// <c>BuyerAcceptedEvent</c> atomically with SaveChanges.
/// </summary>
public interface ITransactionAcceptanceService
{
    Task<AcceptTransactionOutcome> AcceptAsync(
        Guid buyerId,
        Guid transactionId,
        AcceptTransactionRequest request,
        CancellationToken cancellationToken);
}
