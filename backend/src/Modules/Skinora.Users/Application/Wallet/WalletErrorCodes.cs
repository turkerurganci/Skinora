namespace Skinora.Users.Application.Wallet;

/// <summary>
/// Stable error-code strings for the wallet endpoints (07 §5.3, §5.4).
/// Mirrored in frontend i18n catalogs (T97) — treat as part of the public API.
/// </summary>
public static class WalletErrorCodes
{
    public const string InvalidWalletAddress = "INVALID_WALLET_ADDRESS";
    public const string SanctionsMatch = "SANCTIONS_MATCH";
    public const string ReAuthRequired = "RE_AUTH_REQUIRED";
    public const string ReAuthTokenInvalid = "RE_AUTH_TOKEN_INVALID";
    public const string ValidationError = "VALIDATION_ERROR";
}
