using Skinora.Shared.Enums;

namespace Skinora.Steam.Application.Admin;

/// <summary>
/// Top-level body for AD10 (07 §9.10). The degraded-account banner is derived
/// client-side from each account's <c>status</c> (so it localizes per UI
/// locale); the previously server-built Turkish <c>warningMessage</c> was
/// removed in WP17 (T103-K4) — it was unused by the frontend and leaked Turkish.
/// </summary>
public sealed record AdminSteamAccountsResponse(
    IReadOnlyList<AdminSteamAccountDto> Accounts);

/// <summary>One row of <c>data.accounts</c> for AD10 (07 §9.10).</summary>
/// <remarks>
/// <para>
/// <c>RestrictionReason</c> is the sidecar reason for the current non-ACTIVE
/// status; <c>FailoverStatus</c> ∈ NONE / RESTRICTED_NEW_TXN_DIVERTED /
/// ACTIVE_TXN_IN_RECOVERY; <c>RecoveryTransactionCount</c> is the number of open
/// (non-RESOLVED) recovery items for the bot. All three are populated live by
/// <see cref="AdminSteamBotQueryService"/> from the T103b-2 recovery domain.
/// </para>
/// <para>
/// <c>DailyTradeOfferLimit</c> is the Steam protocol limit (200 outgoing
/// trade offers per 24h, ToS-fixed); not configurable via SystemSettings.
/// </para>
/// </remarks>
public sealed record AdminSteamAccountDto(
    Guid Id,
    string Name,
    string SteamId,
    PlatformSteamBotStatus Status,
    int EscrowedItemCount,
    int DailyTradeOfferCount,
    int DailyTradeOfferLimit,
    DateTime? LastHealthCheck,
    string? RestrictionReason,
    string FailoverStatus,
    int RecoveryTransactionCount);
