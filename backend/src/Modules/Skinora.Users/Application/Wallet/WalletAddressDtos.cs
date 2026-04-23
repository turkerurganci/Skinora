namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Request body for <c>PUT /users/me/wallet/seller</c> (07 §5.3) and
/// <c>PUT /users/me/wallet/refund</c> (07 §5.4).
/// </summary>
public sealed record UpdateWalletRequest(string? WalletAddress);

/// <summary>
/// Response body for 07 §5.3 / §5.4 on success — walletAddress echoed back,
/// updatedAt is the User row's new <c>UpdatedAt</c>, and
/// <c>activeTransactionsUsingOldAddress</c> reports how many non-terminal
/// transactions still reference the pre-change address (snapshot principle,
/// 02 §12.3).
/// </summary>
public sealed record UpdateWalletResponse(
    string WalletAddress,
    DateTime UpdatedAt,
    int ActiveTransactionsUsingOldAddress);
