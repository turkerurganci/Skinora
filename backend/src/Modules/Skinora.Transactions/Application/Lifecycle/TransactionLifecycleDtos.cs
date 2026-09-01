using System.Text.Json.Serialization;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Lifecycle;

// ---------- GET /transactions/eligibility (07 §7.3) ----------

/// <summary>Eligibility envelope returned by <c>GET /transactions/eligibility</c> (07 §7.3).</summary>
public sealed record EligibilityDto(
    bool Eligible,
    bool MobileAuthenticatorActive,
    EligibilityConcurrentLimit ConcurrentLimit,
    EligibilityCancelCooldown CancelCooldown,
    EligibilityNewAccountLimit NewAccountLimit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Reasons);

public sealed record EligibilityConcurrentLimit(int Current, int Max);

public sealed record EligibilityCancelCooldown(bool Active, DateTime? ExpiresAt);

public sealed record EligibilityNewAccountLimit(bool IsNewAccount, int? Current, int? Max);

// ---------- GET /transactions/params (07 §7.4) ----------

/// <summary>
/// Form parameters envelope returned by <c>GET /transactions/params</c> (07 §7.4).
/// Prices are emitted as strings to preserve scale-2 fidelity; commission rate
/// is a fraction (0.02) per the M1 closure.
/// </summary>
public sealed record TransactionParamsDto(
    string MinPrice,
    string MaxPrice,
    decimal CommissionRate,
    PaymentTimeoutWindowDto PaymentTimeout,
    bool OpenLinkEnabled,
    IReadOnlyList<string> SupportedStablecoins);

public sealed record PaymentTimeoutWindowDto(int MinHours, int MaxHours, int DefaultHours);

// ---------- POST /transactions (07 §7.2) ----------

/// <summary>
/// Request body for <c>POST /transactions</c> (07 §7.2).
///
/// <para>
/// Carries NO payout address. The seller's payout address is read from the
/// profile (<c>User.DefaultPayoutAddress</c>, written only by U3
/// <c>PUT /users/me/wallet/seller</c>) so that the two controls 02 §12.3
/// assigns to it — Steam re-authentication and the
/// <c>wallet.payout_address_cooldown_hours</c> window — actually guard the
/// value that gets paid. While this record carried the address, both controls
/// sat on the profile write path and the body bypassed them.
/// </para>
/// </summary>
public sealed record CreateTransactionRequest(
    string ItemAssetId,
    StablecoinType Stablecoin,
    string Price,
    int PaymentTimeoutHours,
    BuyerIdentificationMethod BuyerIdentificationMethod,
    string? BuyerSteamId);

/// <summary>Response body for <c>POST /transactions</c> (07 §7.2).</summary>
public sealed record CreateTransactionResponse(
    Guid Id,
    TransactionStatus Status,
    string InviteUrl,
    DateTime CreatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FlagReason);

// ---------- Outcome record (controller maps to ActionResult) ----------

/// <summary>
/// Outcome of <see cref="ITransactionCreationService.CreateAsync"/>. The
/// controller pattern-matches on <see cref="Status"/> to produce 201 / 4xx
/// responses without leaking implementation details.
/// </summary>
public sealed record CreateTransactionOutcome(
    CreateTransactionStatus Status,
    CreateTransactionResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

public enum CreateTransactionStatus
{
    Created,
    ValidationFailed,
    EligibilityFailed,
    InvalidWallet,
    SanctionsMatch,
    OpenLinkDisabled,

    /// <summary>422 <c>ITEM_NOT_IN_INVENTORY</c> — the seller's inventory was
    /// read and the asset is not in it. This is a positive finding, so it must
    /// never be produced from an unreadable inventory (T121, 08 §2.3).</summary>
    ItemNotInInventory,

    /// <summary>422 <c>INVENTORY_PRIVATE</c> — the seller's Steam inventory is
    /// hidden, so nothing can be said about the asset. Same code the inventory
    /// listing endpoint already uses (07 §6.1); the seller's fix is to make the
    /// profile public, which "item not in inventory" would never have told
    /// them (T121).</summary>
    InventoryPrivate,

    /// <summary>503 <c>STEAM_UNAVAILABLE</c> — Steam could not be reached, so
    /// the inventory check is undecided and retryable. Mirrors the accept
    /// endpoint's fail-closed 503 (07 §7.6, T119a) rather than reporting an
    /// outage as a missing item (T121).</summary>
    SteamUnavailable,

    ItemNotTradeable,

    /// <summary>422 <c>ITEM_ALREADY_LISTED</c> — the seller already has a
    /// non-terminal transaction for this asset (02 §2.3, T128). Produced both
    /// by the pre-insert gate and by the unique-index collision two concurrent
    /// creates can still reach; the seller sees one answer either way.</summary>
    ItemAlreadyListed,

    SellerNotFound,
    PriceOutOfRange,
    TimeoutOutOfRange,
    BuyerSteamIdNotFound,
    PayoutAddressCooldownActive,
    SellerWalletAddressMissing,
}

// ---------- POST /transactions/:id/accept (07 §7.6) ----------

/// <summary>
/// Request body for <c>POST /transactions/:id/accept</c> (07 §7.6).
/// <para>
/// T119a — <paramref name="SteamTradeUrl"/> is <b>mandatory as of v3.0</b>: in
/// the P2P model the seller sends the item straight to this address (02 §2.2
/// step 6), so acceptance without it would leave
/// <c>Transaction.BuyerTradeUrl</c> permanently NULL and the seller's delivery
/// CTA empty. Declared as a positional (non-optional) parameter on purpose —
/// every existing caller becomes a compile error rather than a silent runtime
/// 400.
/// </para>
/// </summary>
public sealed record AcceptTransactionRequest(string RefundWalletAddress, string SteamTradeUrl);

/// <summary>Response body for <c>POST /transactions/:id/accept</c> (07 §7.6).</summary>
public sealed record AcceptTransactionResponse(TransactionStatus Status, DateTime AcceptedAt);

/// <summary>
/// Outcome of <see cref="ITransactionAcceptanceService.AcceptAsync"/>. The
/// controller pattern-matches on <see cref="Status"/> to produce 200 / 4xx
/// responses without leaking implementation details.
/// </summary>
public sealed record AcceptTransactionOutcome(
    AcceptTransactionStatus Status,
    AcceptTransactionResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

public enum AcceptTransactionStatus
{
    Accepted,
    NotFound,
    NotAParty,
    SteamIdMismatch,
    AlreadyAccepted,
    InvalidStateTransition,
    ValidationFailed,
    InvalidWallet,
    SanctionsMatch,
    WalletCooldownActive,
    BuyerNotFound,
    AccountFlagged,

    // T119a — 07 §7.6 v3.0 validations.
    /// <summary>400 <c>INVALID_TRADE_URL</c> — malformed URL, or its
    /// <c>partner</c> does not resolve to the caller's own SteamID.</summary>
    InvalidTradeUrl,

    /// <summary>403 <c>MOBILE_AUTHENTICATOR_REQUIRED</c> — the buyer's Steam
    /// trade hold is non-zero (02 §9.1).</summary>
    MobileAuthenticatorRequired,

    /// <summary>503 <c>STEAM_UNAVAILABLE</c> — the trade-hold probe could not
    /// reach Steam, so the Mobile Authenticator state is unknown. Fail-closed
    /// per 08 §2.2; mirrors the 07 §7.6a confirm-ready contract.</summary>
    SteamUnavailable,
}

// ---------- POST /transactions/:id/confirm-ready (07 §7.6a) ----------

/// <summary>
/// Response body for <c>POST /transactions/:id/confirm-ready</c> (07 §7.6a).
/// </summary>
/// <param name="BuyerInventoryVisible">
/// Whether the delivery baseline could be taken (03 §2.3 step 3). Emitted on
/// every response, not only when false: the seller is being told which of the
/// two 02 §9.2 evidence paths will exist for this transaction, and a field that
/// appears only in the bad case is one the client can forget to read. False
/// does <em>not</em> mean the confirmation failed — the transaction advances
/// either way; it means delivery can afterwards only be proven by the buyer's
/// own confirmation.
/// </param>
public sealed record ConfirmReadyResponse(
    TransactionStatus Status,
    DateTime SellerReadyConfirmedAt,
    DateTime PaymentDeadline,
    bool BuyerInventoryVisible);

/// <summary>
/// Outcome of <see cref="ITransactionReadinessService.ConfirmReadyAsync"/>. The
/// controller pattern-matches on <see cref="Status"/> to produce 200 / 4xx / 503
/// responses without leaking implementation details.
/// </summary>
public sealed record ConfirmReadyOutcome(
    ConfirmReadyStatus Status,
    ConfirmReadyResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

public enum ConfirmReadyStatus
{
    Confirmed,
    NotFound,

    /// <summary>403 <c>NOT_A_PARTY</c> — only the seller may confirm readiness.</summary>
    NotAParty,

    /// <summary>409 <c>INVALID_STATE_TRANSITION</c> — not in ACCEPTED.</summary>
    InvalidStateTransition,

    /// <summary>409 <c>ITEM_NO_LONGER_AVAILABLE</c> — inventory read, asset
    /// gone or untradeable (07 §7.6a).</summary>
    ItemNoLongerAvailable,

    /// <summary>422 <c>INVENTORY_PRIVATE</c> — the SELLER's inventory is hidden,
    /// so the item check is undecided. Not a 409: "hidden" is absence of
    /// information, and collapsing it onto ITEM_NO_LONGER_AVAILABLE is the
    /// exact conflation T121 removed. Retrying does not help (08 §2.7) — the
    /// seller has to open their profile, which only this code tells them.
    /// </summary>
    InventoryPrivate,

    /// <summary>403 <c>BUYER_MOBILE_AUTHENTICATOR_INACTIVE</c> — the buyer's MA
    /// is off, so the seller's trade would land in Steam's 15-day escrow
    /// (02 §9.1).</summary>
    BuyerMobileAuthenticatorInactive,

    /// <summary>503 <c>STEAM_UNAVAILABLE</c> — Steam could not be reached for
    /// the item check or the trade-hold probe. Fail-closed and retryable
    /// (08 §2.2); the buyer-baseline read is explicitly NOT part of this — an
    /// unreadable buyer inventory never blocks (03 §2.3 step 3).</summary>
    SteamUnavailable,
}

// ---------- POST /transactions/:id/cancel (07 §7.7) ----------

/// <summary>Request body for <c>POST /transactions/:id/cancel</c> (07 §7.7).</summary>
public sealed record CancelTransactionRequest(string Reason);

/// <summary>
/// Response body for <c>POST /transactions/:id/cancel</c> (07 §7.7).
/// <c>ItemReturned</c> was dropped in v3.0 — the platform never holds the item,
/// so a cancellation can only move money (02 §9).
/// </summary>
public sealed record CancelTransactionResponse(
    TransactionStatus Status,
    DateTime CancelledAt,
    bool PaymentRefunded);

/// <summary>
/// Outcome of <see cref="ITransactionCancellationService.CancelAsync"/>. The
/// controller pattern-matches on <see cref="Status"/> to produce 200 / 4xx
/// responses.
/// </summary>
public sealed record CancelTransactionOutcome(
    CancelTransactionStatus Status,
    CancelTransactionResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

public enum CancelTransactionStatus
{
    Cancelled,
    NotFound,
    NotAParty,
    PaymentAlreadySent,
    InvalidStateTransition,
    ValidationFailed,
    // T105a — caller's account is suspended (restricted session, 02 §14.0).
    AccountSuspended,
}
