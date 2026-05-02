namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Error code constants returned by the T45 transaction-creation pipeline.
/// Mirrors the strings listed under 07 §7.2 "Hatalar".
/// </summary>
public static class TransactionErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InvalidWalletAddress = "INVALID_WALLET_ADDRESS";
    public const string SanctionsMatch = "SANCTIONS_MATCH";
    public const string ConcurrentLimitReached = "CONCURRENT_LIMIT_REACHED";
    public const string CancelCooldownActive = "CANCEL_COOLDOWN_ACTIVE";
    public const string NewAccountLimitReached = "NEW_ACCOUNT_LIMIT_REACHED";
    public const string MobileAuthenticatorRequired = "MOBILE_AUTHENTICATOR_REQUIRED";
    public const string ItemNotTradeable = "ITEM_NOT_TRADEABLE";
    public const string ItemNotInInventory = "ITEM_NOT_IN_INVENTORY";
    public const string PriceOutOfRange = "PRICE_OUT_OF_RANGE";
    public const string TimeoutOutOfRange = "TIMEOUT_OUT_OF_RANGE";
    public const string OpenLinkDisabled = "OPEN_LINK_DISABLED";
    public const string BuyerSteamIdNotFound = "BUYER_STEAM_ID_NOT_FOUND";
    public const string AccountFlagged = "ACCOUNT_FLAGGED";
    public const string PayoutAddressCooldownActive = "PAYOUT_ADDRESS_COOLDOWN_ACTIVE";
    public const string SellerWalletAddressMissing = "SELLER_WALLET_ADDRESS_MISSING";

    // Eligibility-only reason codes (07 §7.3 reasons array). Mirrors the
    // CreateTransaction error codes so callers can use a single switch.
    public static class EligibilityReasons
    {
        public const string ConcurrentLimitReached = TransactionErrorCodes.ConcurrentLimitReached;
        public const string CancelCooldownActive = TransactionErrorCodes.CancelCooldownActive;
        public const string NewAccountLimitReached = TransactionErrorCodes.NewAccountLimitReached;
        public const string MobileAuthenticatorRequired = TransactionErrorCodes.MobileAuthenticatorRequired;
        public const string AccountFlagged = TransactionErrorCodes.AccountFlagged;
        public const string PayoutAddressCooldownActive = TransactionErrorCodes.PayoutAddressCooldownActive;
        public const string SellerWalletAddressMissing = TransactionErrorCodes.SellerWalletAddressMissing;
    }
}
