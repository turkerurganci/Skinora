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

/// <summary>Request body for <c>POST /transactions</c> (07 §7.2).</summary>
public sealed record CreateTransactionRequest(
    string ItemAssetId,
    StablecoinType Stablecoin,
    string Price,
    int PaymentTimeoutHours,
    BuyerIdentificationMethod BuyerIdentificationMethod,
    string? BuyerSteamId,
    string SellerWalletAddress);

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
    ItemNotInInventory,
    ItemNotTradeable,
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
