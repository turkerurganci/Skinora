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

    /// <summary>
    /// Resolves an OPEN_LINK invitation by its opaque token and builds the
    /// public-invite consume surface (04 §7.3 / 03 §3.2 step 1). Mirrors
    /// <see cref="GetAsync"/> but keys on <c>Transaction.InviteToken</c>
    /// instead of the id, so the seller-shared <c>/invite/:token</c> link
    /// resolves without leaking the enumerable transaction id.
    /// </summary>
    /// <remarks>
    /// Role resolution differs from the id path: an authenticated caller who
    /// is neither party and the invite is still joinable (CREATED, no buyer)
    /// is treated as a <b>prospective buyer</b> — they receive the buyer
    /// acceptance surface (<c>canAccept=true</c>, accept stays id-based via
    /// <c>POST /transactions/:id/accept</c>). Unauthenticated callers get the
    /// trimmed public shape with <c>requiresLogin=true</c>. Spent / non-CREATED
    /// invites fall back to the trimmed public shape for non-parties.
    /// </remarks>
    Task<TransactionDetailOutcome> GetByInviteTokenAsync(
        string inviteToken,
        Guid? callerId,
        string? callerSteamId,
        CancellationToken cancellationToken);
}
