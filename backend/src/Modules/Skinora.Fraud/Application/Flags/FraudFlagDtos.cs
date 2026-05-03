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
    FraudFlagScope Scope,
    FraudFlagType Type,
    ReviewStatus ReviewStatus,
    DateTime CreatedAt,
    object? FlagDetail,
    FlagTransactionDto? Transaction,
    FlagPartyDto? Seller,
    FlagPartyDto? Buyer,
    int HistoricalTransactionCount,
    Guid? ReviewedByAdminId,
    DateTime? ReviewedAt,
    string? AdminNote);

/// <summary>Lightweight party view used by AD2/AD3 (07 §9.2/§9.3).</summary>
public sealed record FlagPartyDto(
    string SteamId,
    string DisplayName,
    string? AvatarUrl);

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
    IReadOnlyList<MultiAccountLinkedAccount> LinkedAccounts);

/// <summary>Linked account entry inside <see cref="MultiAccountFlagDetail"/>.</summary>
public sealed record MultiAccountLinkedAccount(
    string SteamId,
    string DisplayName);
