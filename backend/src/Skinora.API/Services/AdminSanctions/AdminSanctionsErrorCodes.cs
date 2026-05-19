namespace Skinora.API.Services.AdminSanctions;

/// <summary>
/// Error codes returned by the admin sanctions list endpoints
/// (07 §9.23–§9.25 AD22/AD23/AD24). Stable strings — consumed by frontend
/// + integration tests.
/// </summary>
public static class AdminSanctionsErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InvalidWalletAddress = "INVALID_WALLET_ADDRESS";
    public const string AlreadyListed = "SANCTIONS_ADDRESS_ALREADY_LISTED";
    public const string NotFound = "SANCTIONS_ADDRESS_NOT_FOUND";
    public const string AlreadyInactive = "SANCTIONS_ADDRESS_ALREADY_INACTIVE";
}
