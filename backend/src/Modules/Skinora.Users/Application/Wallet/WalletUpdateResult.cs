namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Outcome of <see cref="IWalletAddressService.UpdateWalletAsync"/>.
/// Controller maps each <see cref="WalletUpdateStatus"/> to the HTTP
/// response code / error code defined in 07 §5.3.
/// </summary>
public enum WalletUpdateStatus
{
    Success,

    /// <summary>User missing / soft-deleted / deactivated — treat as 401.</summary>
    UserNotFound,

    /// <summary>07 §5.3 — 400 <c>INVALID_WALLET_ADDRESS</c>.</summary>
    InvalidAddress,

    /// <summary>07 §5.3 — 403 <c>SANCTIONS_MATCH</c>.</summary>
    SanctionsMatch,

    /// <summary>07 §5.3 — 403 <c>RE_AUTH_REQUIRED</c> (existing address, caller omitted <c>X-ReAuth-Token</c>).</summary>
    ReAuthRequired,
}

/// <summary>
/// Structured outcome of a wallet address update attempt. On
/// <see cref="WalletUpdateStatus.Success"/>, all nullable fields are populated
/// and the caller echoes them into the 07 §5.3 response. For non-success
/// statuses the fields may be null; the controller uses only the status.
/// </summary>
public sealed record WalletUpdateResult(
    WalletUpdateStatus Status,
    string? WalletAddress,
    DateTime? UpdatedAt,
    int ActiveTransactionsUsingOldAddress,
    string? SanctionsList)
{
    public static WalletUpdateResult Success(
        string walletAddress, DateTime updatedAt, int activeTransactionsUsingOldAddress)
        => new(WalletUpdateStatus.Success, walletAddress, updatedAt, activeTransactionsUsingOldAddress, null);

    public static WalletUpdateResult Failure(WalletUpdateStatus status, string? sanctionsList = null)
        => new(status, null, null, 0, sanctionsList);
}
