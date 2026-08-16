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

    /// <summary>
    /// T128 — 422 <c>ITEM_ALREADY_LISTED</c>: the seller already has a
    /// non-terminal transaction for this asset (02 §2.3). Not a courtesy
    /// check — delivery evidence is measured at the item-class level
    /// (02 §9.2), so two live transactions over one asset would let an
    /// arriving item be attributed to the wrong one and pay the wrong
    /// seller. The database says the same thing through
    /// <c>UQ_Transactions_SellerId_ItemAssetId_Active</c> (06 §5.1); this
    /// code is how the rule reaches the seller instead of a 500.
    /// </summary>
    public const string ItemAlreadyListed = "ITEM_ALREADY_LISTED";

    // T121 — the create path re-reads the seller's inventory, so it can hit the
    // same two non-readable outcomes the listing endpoint already reports
    // (07 §6.1). Same strings, same meaning; declared here as literals because
    // Skinora.Transactions does not reference Skinora.Steam (the dependency
    // runs the other way), exactly as SteamUnavailable below already does.
    public const string InventoryPrivate = "INVENTORY_PRIVATE";
    public const string PriceOutOfRange = "PRICE_OUT_OF_RANGE";
    public const string TimeoutOutOfRange = "TIMEOUT_OUT_OF_RANGE";
    public const string OpenLinkDisabled = "OPEN_LINK_DISABLED";
    public const string BuyerSteamIdNotFound = "BUYER_STEAM_ID_NOT_FOUND";
    public const string AccountFlagged = "ACCOUNT_FLAGGED";
    // T105a — suspended account cannot take fund/asset-flow action.
    public const string AccountSuspended = "ACCOUNT_SUSPENDED";
    public const string PayoutAddressCooldownActive = "PAYOUT_ADDRESS_COOLDOWN_ACTIVE";
    public const string SellerWalletAddressMissing = "SELLER_WALLET_ADDRESS_MISSING";

    // T46 — detail / accept (07 §7.5–§7.6).
    public const string TransactionNotFound = "TRANSACTION_NOT_FOUND";
    public const string NotAParty = "NOT_A_PARTY";
    public const string SteamIdMismatch = "STEAM_ID_MISMATCH";
    public const string AlreadyAccepted = "ALREADY_ACCEPTED";
    public const string InvalidStateTransition = "INVALID_STATE_TRANSITION";
    public const string WalletChangeCooldownActive = "WALLET_CHANGE_COOLDOWN_ACTIVE";
    public const string RefundAddressRequired = "REFUND_ADDRESS_REQUIRED";

    // T119a — accept v3.0 fields (07 §7.6). MobileAuthenticatorRequired is
    // declared above (create path, 07 §7.2) and reused verbatim here.
    public const string InvalidTradeUrl = "INVALID_TRADE_URL";
    public const string SteamUnavailable = "STEAM_UNAVAILABLE";

    // T123 — confirm-ready (07 §7.6a).
    /// <summary>
    /// 409 — the seller's inventory was READ and the listed asset is either
    /// gone or no longer tradeable. A positive finding, so it may only be
    /// produced from an <c>InventoryVisibility.Public</c> read (08 §2.3).
    /// </summary>
    public const string ItemNoLongerAvailable = "ITEM_NO_LONGER_AVAILABLE";

    /// <summary>
    /// 403 — the BUYER's Steam Mobile Authenticator is inactive (02 §9.1).
    /// Distinct from <see cref="MobileAuthenticatorRequired"/> on purpose: here
    /// the caller is the seller and the fix belongs to the other party, so the
    /// two cases must not share a code the UI would phrase as "enable your
    /// authenticator" (07 §7.6a).
    /// </summary>
    public const string BuyerMobileAuthenticatorInactive = "BUYER_MOBILE_AUTHENTICATOR_INACTIVE";

    // T51 — cancel (07 §7.7).
    public const string PaymentAlreadySent = "PAYMENT_ALREADY_SENT";
    public const string CancelReasonRequired = "CANCEL_REASON_REQUIRED";

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
