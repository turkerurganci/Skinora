namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Builds the response for <c>GET /transactions/:id</c> (T46 — 07 §7.5).
/// Selects between the public and authenticated views based on
/// <paramref name="callerId"/>; the authenticated view further branches on
/// the role (seller / buyer / non-party). State-blocked sections
/// (payment, sellerPayout, refund, etc.) stay <c>null</c> until the owning
/// task ships.
/// </summary>
public interface ITransactionDetailService
{
    /// <summary>
    /// </summary>
    /// <param name="callerSteamId">
    /// The caller's Steam ID 64 (from the JWT). Used to resolve the
    /// "target buyer before acceptance" case for STEAM_ID-method
    /// transactions where <c>Transaction.BuyerId</c> is still <c>null</c>
    /// — the named buyer must be able to view the detail to decide
    /// whether to accept (03 §3.2 step 1).
    /// </param>
    Task<TransactionDetailOutcome> GetAsync(
        Guid transactionId,
        Guid? callerId,
        string? callerSteamId,
        CancellationToken cancellationToken);
}
