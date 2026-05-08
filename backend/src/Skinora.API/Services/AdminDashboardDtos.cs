using Skinora.Steam.Application.Admin;

namespace Skinora.API.Services;

/// <summary>Top-level body for AD1 (07 §9.1).</summary>
public sealed record AdminDashboardResponse(
    AdminDashboardSummaryCardsDto SummaryCards,
    IReadOnlyList<AdminSteamAccountDto> SteamAccounts,
    IReadOnlyList<AdminDashboardRecentFlagDto> RecentFlags);

/// <summary>Header counters for AD1 (07 §9.1).</summary>
public sealed record AdminDashboardSummaryCardsDto(
    int ActiveTransactions,
    int PendingFlags,
    int DailyCompleted,
    int WeeklyCompleted);

/// <summary>One row of <c>recentFlags</c> (07 §9.1) — last 5 flags.</summary>
public sealed record AdminDashboardRecentFlagDto(
    Guid Id,
    Guid? TransactionId,
    string Type,
    string ReviewStatus,
    DateTime CreatedAt);
