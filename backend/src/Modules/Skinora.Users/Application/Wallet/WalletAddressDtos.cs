namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Request body for <c>PUT /users/me/wallet/seller</c> (07 §5.3) and
/// <c>PUT /users/me/wallet/refund</c> (07 §5.4).
/// </summary>
/// <param name="WalletAddress">The address to save.</param>
/// <param name="Network">
/// <c>"TRON"</c> or <c>"ETHEREUM"</c> (07 §1.3, T146). Omitted means
/// <c>TRON</c> — the pre-F8 body keeps working until the network-aware UI
/// (T156) sends it on every call.
/// </param>
/// <param name="ConfirmLowercase">
/// The user's second confirmation for an EVM address written all-lowercase or
/// all-uppercase, which carries no EIP-55 checksum (07 §1.3). Without it such
/// an address is rejected with 400 <c>INVALID_WALLET_ADDRESS</c>,
/// <c>details.reason = "CHECKSUM_CONFIRMATION_REQUIRED"</c>. Ignored for TRON.
/// </param>
public sealed record UpdateWalletRequest(
    string? WalletAddress,
    string? Network = null,
    bool? ConfirmLowercase = null);

/// <summary>
/// Response body for 07 §5.3 / §5.4 on success.
/// </summary>
/// <param name="WalletAddress">
/// The saved address in display form (06 §1.4): an Ethereum address in its
/// EIP-55 mixed-case spelling, a TRON address unchanged.
/// </param>
/// <param name="Network">The network the address was saved for.</param>
/// <param name="UpdatedAt">When the address was set — the start of its cooldown window (02 §12.3).</param>
/// <param name="ActiveTransactionsUsingOldAddress">
/// Non-terminal transactions still referencing the replaced address of the
/// same network and type (snapshot principle, 02 §12.3).
/// </param>
/// <param name="ChecksumVerified">
/// <c>true</c> when a checksum protected the input (TRON base58check, or a
/// mixed-case EIP-55 Ethereum address); <c>false</c> when an all-lowercase /
/// all-uppercase Ethereum address was accepted on the user's confirmation.
/// </param>
public sealed record UpdateWalletResponse(
    string WalletAddress,
    string Network,
    DateTime UpdatedAt,
    int ActiveTransactionsUsingOldAddress,
    bool ChecksumVerified);

/// <summary>
/// One saved address in <c>GET /users/me</c> <c>wallets[]</c> (07 §5.1, T146).
/// </summary>
/// <param name="Network"><c>"TRON"</c> / <c>"ETHEREUM"</c>.</param>
/// <param name="Type"><c>"PAYOUT"</c> / <c>"REFUND"</c>.</param>
/// <param name="Address">Display form (EIP-55 for Ethereum).</param>
/// <param name="ChangedAt">Last change; <c>null</c> for an address that predates change tracking.</param>
public sealed record UserWalletAddressDto(
    string Network,
    string Type,
    string Address,
    DateTime? ChangedAt);
