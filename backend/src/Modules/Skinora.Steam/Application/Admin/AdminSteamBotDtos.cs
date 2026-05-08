using Skinora.Shared.Enums;

namespace Skinora.Steam.Application.Admin;

/// <summary>Top-level body for AD10 (07 §9.10).</summary>
public sealed record AdminSteamAccountsResponse(
    IReadOnlyList<AdminSteamAccountDto> Accounts,
    string? WarningMessage);

/// <summary>One row of <c>data.accounts</c> for AD10 (07 §9.10).</summary>
/// <remarks>
/// <para>
/// <c>FailoverStatus</c>, <c>RecoveryTransactionCount</c> and
/// <c>RestrictionReason</c> are forward-deferred to T69 (Steam Sidecar
/// failover + capacity-based selection). Until T69 wires the bot health
/// pipeline, every row reports <c>"NONE"</c> / <c>0</c> / <c>null</c>
/// — see <see cref="AdminSteamBotQueryService"/>.
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
