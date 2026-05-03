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

/// <summary>Request body for <c>POST /transactions/:id/accept</c> (07 §7.6).</summary>
public sealed record AcceptTransactionRequest(string RefundWalletAddress);

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
}

// ---------- POST /transactions/:id/cancel (07 §7.7) ----------

/// <summary>Request body for <c>POST /transactions/:id/cancel</c> (07 §7.7).</summary>
public sealed record CancelTransactionRequest(string Reason);

/// <summary>Response body for <c>POST /transactions/:id/cancel</c> (07 §7.7).</summary>
public sealed record CancelTransactionResponse(
    TransactionStatus Status,
    DateTime CancelledAt,
    bool ItemReturned,
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
}
