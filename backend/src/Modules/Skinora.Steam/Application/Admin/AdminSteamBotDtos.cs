using Skinora.Shared.Enums;

namespace Skinora.Steam.Application.Admin;

/// <summary>Top-level body for AD10 (07 §9.10).</summary>
public sealed record AdminSteamAccountsResponse(
    IReadOnlyList<AdminSteamAccountDto> Accounts,
    string? WarningMessage);

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
