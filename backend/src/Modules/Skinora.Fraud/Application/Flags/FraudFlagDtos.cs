using System.Text.Json.Serialization;
using Skinora.Shared.Enums;

namespace Skinora.Fraud.Application.Flags;

/// <summary>One row in the <c>GET /admin/flags</c> response (07 §9.2).</summary>
public sealed record FraudFlagListItemDto(
    Guid Id,
    Guid? TransactionId,
    FraudFlagScope Scope,
    FraudFlagType Type,
    ReviewStatus ReviewStatus,
    FlagPartyDto? Seller,
    string? ItemName,
    decimal? Price,
    StablecoinType? Stablecoin,
    decimal? MarketPrice,
    // K2 (07 §9.2, 04 §8.2 hesap-flag kolonları) — populated only for
    // ACCOUNT_LEVEL rows (null for transaction flags). SignalSummary is the
    // raw matched identifier (wallet address / pattern) — non-translatable, so
    // the frontend just labels the column; the full IP/device evidence lives in
    // the AD3 detail (supportingSignals). LinkedAccountCount comes from the
    // parsed flagDetail; ActiveTransactionCount is the per-user active count.
    string? SignalSummary,
    int? LinkedAccountCount,
    int? ActiveTransactionCount,
    DateTime CreatedAt);

/// <summary>
/// Page envelope for AD2 — wraps the standard
/// <see cref="Skinora.Shared.Models.PagedResult{T}"/> output with the badge
/// <c>pendingCount</c> required by 07 §9.2.
/// </summary>
public sealed record FraudFlagListResponse(
    IReadOnlyList<FraudFlagListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int PendingCount);

/// <summary>Detail body for AD3 (07 §9.3).</summary>
public sealed record FraudFlagDetailDto(
    Guid Id,
    // Internal id of the flagged user — lets the admin UI target user-level
    // actions (e.g. the §8.3 "Hold" bulk emergency-hold, AD19d) without a
    // separate steamId→id lookup round-trip.
    Guid UserId,
    FraudFlagScope Scope,
    FraudFlagType Type,
    ReviewStatus ReviewStatus,
    DateTime CreatedAt,
    object? FlagDetail,
    FlagTransactionDto? Transaction,
    FlagPartyDetailDto? Seller,
    FlagPartyDetailDto? Buyer,
    int HistoricalTransactionCount,
    // K9 (07 §9.3, 04 §8.3 hesap-flag madde 4) — the flagged user's current
    // active (non-terminal) transactions; count = list length. Populated for
    // every flag but primarily consumed by the account-flag S14 variant.
    IReadOnlyList<FlagActiveTransactionDto> ActiveTransactions,
    [property: JsonPropertyName("reviewedBy")] Guid? ReviewedByAdminId,
    DateTime? ReviewedAt,
    string? AdminNote);

/// <summary>
/// One active (non-terminal) transaction of the flagged user, surfaced by AD3
/// for the account-flag S14 variant (K9 — 04 §8.3 "Aktif İşlemler"). "Active"
/// mirrors the AD19d hold predicate (07 §9.22a): any non-terminal status
/// (<see cref="TransactionStatus.FLAGGED"/> included), either party.
/// <see cref="IsOnHold"/> tells the admin which rows a subsequent bulk-hold
/// would skip (the hold is idempotent and only affects the non-held subset).
/// </summary>
public sealed record FlagActiveTransactionDto(
    Guid Id,
    TransactionStatus Status,
    string ItemName,
    decimal Price,
    StablecoinType Stablecoin,
    FlagTransactionRole Role,
    bool IsOnHold,
    DateTime CreatedAt);

/// <summary>Role of the flagged user in a <see cref="FlagActiveTransactionDto"/> (07 §9.3).</summary>
public enum FlagTransactionRole
{
    SELLER,
    BUYER,
}

/// <summary>Lightweight party view used by AD2 list (07 §9.2).</summary>
public sealed record FlagPartyDto(
    string SteamId,
    string DisplayName,
    string? AvatarUrl);

/// <summary>
/// Rich party view used by AD3 detail (07 §9.3) — adds the user-trust signals
/// admins inspect alongside the flag (denormalized <see cref="Skinora.Users.Domain.Entities.User.CompletedTransactionCount"/>,
/// composite reputation via <c>IReputationScoreCalculator</c>, and the
/// Turkish-formatted account age from <c>AccountAgeFormatter</c>).
/// </summary>
public sealed record FlagPartyDetailDto(
    string SteamId,
    string DisplayName,
    string? AvatarUrl,
    decimal? ReputationScore,
    int CompletedTransactionCount,
    string AccountAge);

/// <summary>Embedded transaction view returned by AD3 (07 §9.3).</summary>
public sealed record FlagTransactionDto(
    Guid Id,
    TransactionStatus Status,
    string ItemName,
    string? ItemImageUrl,
    decimal Price,
    StablecoinType Stablecoin,
    int PaymentTimeoutHours,
    DateTime CreatedAt);

/// <summary>Body of the AD4 / AD5 success response (07 §9.4 / §9.5).</summary>
public sealed record FraudFlagReviewResultDto(
    ReviewStatus ReviewStatus,
    TransactionStatus? TransactionStatus,
    DateTime ReviewedAt);

/// <summary>Request body for <c>POST /admin/flags/:id/approve</c> and <c>/reject</c>.</summary>
public sealed record FraudFlagReviewRequest(string? Note);

// ── Type-specific FlagDetail payloads (07 §9.3 table) ────────────────────────

/// <summary><c>flagDetail</c> shape for <see cref="FraudFlagType.PRICE_DEVIATION"/>.</summary>
public sealed record PriceDeviationFlagDetail(
    decimal InputPrice,
    decimal MarketPrice,
    decimal DeviationPercent);

/// <summary><c>flagDetail</c> shape for <see cref="FraudFlagType.HIGH_VOLUME"/>.</summary>
public sealed record HighVolumeFlagDetail(
    int PeriodHours,
    int TransactionCount,
    decimal TotalVolume);

/// <summary><c>flagDetail</c> shape for <see cref="FraudFlagType.ABNORMAL_BEHAVIOR"/>.</summary>
public sealed record AbnormalBehaviorFlagDetail(
    string Pattern,
    string Description);

/// <summary><c>flagDetail</c> shape for <see cref="FraudFlagType.MULTI_ACCOUNT"/>.</summary>
public sealed record MultiAccountFlagDetail(
    string MatchType,
    string MatchValue,
    IReadOnlyList<MultiAccountLinkedAccount> LinkedAccounts,
    // K10 (07 §9.3:1742) — supporting evidence (IP_ADDRESS / DEVICE_FINGERPRINT /
    // SOURCE_ADDRESS). MultiAccountDetector already serialises these into
    // FraudFlag.Details; the DTO now deserialises them so S14 can render the
    // IP/device evidence alongside the strong wallet-address signal.
    IReadOnlyList<MultiAccountSupportingSignal> SupportingSignals);

/// <summary>Linked account entry inside <see cref="MultiAccountFlagDetail"/>.</summary>
public sealed record MultiAccountLinkedAccount(
    string SteamId,
    string DisplayName);

/// <summary>
/// Supporting-signal entry inside <see cref="MultiAccountFlagDetail"/> (07 §9.3).
/// <c>Type</c> is one of <c>IP_ADDRESS</c> / <c>DEVICE_FINGERPRINT</c> /
/// <c>SOURCE_ADDRESS</c>; these are evidence only and never flag on their own.
/// </summary>
public sealed record MultiAccountSupportingSignal(
    string Type,
    string Value,
    IReadOnlyList<MultiAccountLinkedAccount> LinkedAccounts);
