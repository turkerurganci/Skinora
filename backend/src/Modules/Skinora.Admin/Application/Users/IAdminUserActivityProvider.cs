namespace Skinora.Admin.Application.Users;

/// <summary>
/// Cross-module read aggregation for AD16 user detail (07 §9.16, 04 §8.9).
/// Pulls the transaction-derived statistics, the conditional status-badge
/// signals, and the flag / dispute / counterparty history for a single user.
/// </summary>
/// <remarks>
/// The implementation lives at the API composition root
/// (<c>Skinora.API.Services</c>) because it fans out into
/// <c>Skinora.Transactions</c>, <c>Skinora.Fraud</c> and
/// <c>Skinora.Disputes</c> — modules that <c>Skinora.Admin</c> cannot
/// reference without a project cycle. This mirrors the constraint that keeps
/// <c>AdminTransactionQueryService</c> (AD7) at the composition root.
/// </remarks>
public interface IAdminUserActivityProvider
{
    Task<AdminUserActivity> GetAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Aggregated cross-module activity for a user. Counts/volume feed
/// <see cref="AdminUserDetailStatsDto"/>; the two badge signals feed the
/// 04 §8.9.1 conditional badges; the three lists feed the matching
/// <see cref="AdminUserDetailDto"/> sections.
/// </summary>
public sealed record AdminUserActivity(
    int TotalTransactions,
    int CompletedTransactions,
    int CancelledTransactions,
    int FlaggedTransactions,
    string? TotalVolume,
    DateTime? LastTransactionAt,
    int ActiveTransactionCount,
    bool HasTransactionOnHold,
    IReadOnlyList<AdminUserFlagEntryDto> FlagHistory,
    IReadOnlyList<AdminUserDisputeEntryDto> DisputeHistory,
    IReadOnlyList<AdminUserCounterpartyDto> FrequentCounterparties);
