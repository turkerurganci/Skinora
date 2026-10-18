using Skinora.Shared.Enums;

namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Outcome of <see cref="IWalletAddressService.UpdateWalletAsync"/>.
/// Controller maps each <see cref="WalletUpdateStatus"/> to the HTTP
/// response code / error code defined in 07 §5.3.
/// </summary>
public enum WalletUpdateStatus
{
    Success,

    /// <summary>User missing / soft-deleted / deactivated / suspended — treat as 401.</summary>
    UserNotFound,

    /// <summary>
    /// 07 §5.3 — 400 <c>INVALID_WALLET_ADDRESS</c>; <see cref="WalletUpdateResult.Reason"/>
    /// is <c>INVALID_FORMAT</c> or <c>CHECKSUM_MISMATCH</c>.
    /// </summary>
    InvalidAddress,

    /// <summary>
    /// 07 §1.3 — 400 <c>INVALID_WALLET_ADDRESS</c> with reason
    /// <c>CHECKSUM_CONFIRMATION_REQUIRED</c>: an EVM address without an EIP-55
    /// checksum and no <c>confirmLowercase: true</c> in the body.
    /// </summary>
    ChecksumConfirmationRequired,

    /// <summary>07 §5.3 — 403 <c>SANCTIONS_MATCH</c>.</summary>
    SanctionsMatch,

    /// <summary>07 §5.3 — 403 <c>RE_AUTH_REQUIRED</c> (existing address on this network, or — T147 — an address of the same type on another network; caller omitted <c>X-ReAuth-Token</c>).</summary>
    ReAuthRequired,
}

/// <summary>
/// Structured outcome of a wallet address update attempt. On
/// <see cref="WalletUpdateStatus.Success"/>, the address fields are populated
/// and the caller echoes them into the 07 §5.3 response. For non-success
/// statuses only <see cref="Status"/>, <see cref="Network"/>,
/// <see cref="Reason"/> and <see cref="SanctionsList"/> are meaningful.
/// </summary>
public sealed record WalletUpdateResult(
    WalletUpdateStatus Status,
    BlockchainNetwork Network,
    string? WalletAddress,
    DateTime? UpdatedAt,
    int ActiveTransactionsUsingOldAddress,
    bool ChecksumVerified,
    string? SanctionsList,
    string? Reason)
{
    public static WalletUpdateResult Success(
        BlockchainNetwork network,
        string walletAddress,
        DateTime updatedAt,
        int activeTransactionsUsingOldAddress,
        bool checksumVerified)
        => new(WalletUpdateStatus.Success, network, walletAddress, updatedAt,
            activeTransactionsUsingOldAddress, checksumVerified, null, null);

    public static WalletUpdateResult Failure(
        WalletUpdateStatus status,
        BlockchainNetwork network,
        string? sanctionsList = null,
        string? reason = null)
        => new(status, network, null, null, 0, false, sanctionsList, reason);
}
