namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Persists a user's default wallet address after running the central
/// validation pipeline (02 §12.3, 03 §9 — TRC-20 format + sanctions
/// screening). Re-auth (Steam re-verify) is checked by the caller before
/// invocation; this service accepts a boolean flag asserting the caller
/// has already validated the <c>X-ReAuth-Token</c> — see 07 §5.3 "Ek Auth".
/// </summary>
public interface IWalletAddressService
{
    /// <summary>
    /// Validates and persists the new wallet address for the given role.
    /// On success, bumps <c>PayoutAddressChangedAt</c> or
    /// <c>RefundAddressChangedAt</c> (cooldown timer start — 02 §12.3) and
    /// returns a count of non-terminal transactions still referencing the
    /// previous address via their snapshot column (02 §12.3).
    /// </summary>
    /// <param name="userId">Authenticated user's id.</param>
    /// <param name="role">Whether this is the seller payout or buyer refund address.</param>
    /// <param name="newAddress">Candidate address; format-checked before sanctions screening.</param>
    /// <param name="reAuthValidated">Whether the caller has already consumed a valid <c>X-ReAuth-Token</c> bound to <paramref name="userId"/>.</param>
    Task<WalletUpdateResult> UpdateWalletAsync(
        Guid userId,
        WalletRole role,
        string? newAddress,
        bool reAuthValidated,
        CancellationToken cancellationToken);
}
