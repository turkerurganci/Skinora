using Skinora.Shared.Enums;

namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Persists a user's payout or refund address for one network after running
/// the central validation pipeline (02 §12.3, 03 §9 — network format +
/// sanctions screening on that network). Re-auth (Steam re-verify) is checked
/// by the caller before invocation; this service accepts a boolean flag
/// asserting the caller has already validated the <c>X-ReAuth-Token</c> —
/// see 07 §5.3 "Ek Auth".
/// </summary>
public interface IWalletAddressService
{
    /// <summary>
    /// Validates and persists the address of <paramref name="role"/> on
    /// <paramref name="network"/> (one <c>UserWalletAddress</c> row, 06 §1.4).
    /// On success, stamps the row's <c>ChangedAt</c> (cooldown timer start for
    /// that network only — 02 §12.3), appends the replaced address to the
    /// history, and returns a count of non-terminal transactions still
    /// referencing the replaced address via their snapshot column.
    /// </summary>
    /// <param name="userId">Authenticated user's id.</param>
    /// <param name="role">Whether this is the seller payout or buyer refund address.</param>
    /// <param name="network">The network the address belongs to.</param>
    /// <param name="newAddress">Candidate address; format-checked before sanctions screening.</param>
    /// <param name="confirmedWithoutChecksum">
    /// The user's second confirmation for an EVM address that carries no
    /// EIP-55 checksum (07 §1.3 <c>confirmLowercase</c>).
    /// </param>
    /// <param name="reAuthValidated">Whether the caller has already consumed a valid <c>X-ReAuth-Token</c> bound to <paramref name="userId"/>.</param>
    Task<WalletUpdateResult> UpdateWalletAsync(
        Guid userId,
        WalletRole role,
        BlockchainNetwork network,
        string? newAddress,
        bool confirmedWithoutChecksum,
        bool reAuthValidated,
        CancellationToken cancellationToken);
}
