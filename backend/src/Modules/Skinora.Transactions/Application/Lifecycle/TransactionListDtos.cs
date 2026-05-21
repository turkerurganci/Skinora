using System.Text.Json.Serialization;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// One row of the T1 list (07 §7.1, T83a). Mirrors the response sample —
/// <c>status</c> is a <see cref="string"/> projection so the EMERGENCY_HOLD
/// overlay (<c>IsOnHold=true</c>, 06 §3.5) surfaces as a computed value
/// alongside the real <see cref="TransactionStatus"/> names. <c>price</c>
/// is serialized as a string with two decimals to match the contract.
/// </summary>
public sealed record TransactionListItemDto(
    Guid Id,
    string ItemName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ItemImageUrl,
    string Status,
    string Price,
    StablecoinType Stablecoin,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TransactionListCounterpartyDto? Counterparty,
    string UserRole,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TransactionListActiveTimeoutDto? ActiveTimeout,
    DateTime CreatedAt);

/// <summary>
/// Counterparty snapshot (07 §7.1) — null when the other party has not
/// registered yet (OPEN_LINK pre-acceptance + seller-side rows where the
/// buyer hasn't accepted).
/// </summary>
public sealed record TransactionListCounterpartyDto(
    string SteamId,
    string DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AvatarUrl);

/// <summary>
/// Active timeout block (07 §7.1). Resolved per the 06 §3.5 state→active-
/// deadline matrix; null for terminal and matrix-blank states.
/// </summary>
public sealed record TransactionListActiveTimeoutDto(
    string Type,
    DateTime ExpiresAt,
    int RemainingSeconds,
    int WarningThresholdPercent);

/// <summary>
/// Tab filter — maps onto the status sets enumerated in 07 §7.1.
/// </summary>
public enum TransactionListTab
{
    Active,
    Completed,
    Cancelled,
}

/// <summary>
/// Query inputs for <see cref="ITransactionListService.ListAsync"/>.
/// </summary>
/// <param name="Tab">
/// Tab filter. The controller substitutes <see cref="TransactionListTab.Active"/>
/// when the caller omits the query parameter (11 §T83a default behaviour).
/// </param>
/// <param name="Page">1-indexed page number. Clamped at the service.</param>
/// <param name="PageSize">Page size. Clamped to 1–100 (default 20) at the service.</param>
public sealed record TransactionListQuery(
    TransactionListTab Tab,
    int Page,
    int PageSize);
