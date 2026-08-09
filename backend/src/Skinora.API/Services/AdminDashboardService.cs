using Microsoft.EntityFrameworkCore;
using Skinora.Fraud.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.API.Services;

/// <inheritdoc cref="IAdminDashboardService"/>
public sealed class AdminDashboardService : IAdminDashboardService
{
    /// <summary>Number of recent flags surfaced on the dashboard (07 §9.1).</summary>
    public const int RecentFlagsLimit = 5;

    /// <summary>
    /// Terminal transaction states. Mirrors <c>TransactionDetailService.IsTerminal</c>
    /// — kept duplicated here to avoid pulling Skinora.Transactions internals
    /// into the API composition root.
    /// </summary>
    private static readonly TransactionStatus[] _terminalStates =
    [
        TransactionStatus.COMPLETED,
        TransactionStatus.CANCELLED_TIMEOUT,
        TransactionStatus.CANCELLED_SELLER,
        TransactionStatus.CANCELLED_BUYER,
        TransactionStatus.CANCELLED_ADMIN,
        TransactionStatus.REFUNDED,
    ];

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public AdminDashboardService(
        AppDbContext db,
        TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<AdminDashboardResponse> GetAsync(CancellationToken cancellationToken)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var dailyCutoff = nowUtc.AddHours(-24);
        var weeklyCutoff = nowUtc.AddDays(-7);

        // Counters: 4 cheap aggregates, each over an indexed predicate.
        // Run sequentially against the same DbContext (EF doesn't support
        // concurrent readers on a single context — and the queries are fast
        // enough that the round-trip cost dominates the parallelism gain).
        var activeTransactions = await _db.Set<Transaction>()
            .AsNoTracking()
            .CountAsync(t => !t.IsDeleted && !_terminalStates.Contains(t.Status),
                cancellationToken);

        var pendingFlags = await _db.Set<FraudFlag>()
            .AsNoTracking()
            .CountAsync(f => f.Status == ReviewStatus.PENDING, cancellationToken);

        var dailyCompleted = await _db.Set<Transaction>()
            .AsNoTracking()
            .CountAsync(t => t.Status == TransactionStatus.COMPLETED
                          && t.CompletedAt != null
                          && t.CompletedAt >= dailyCutoff,
                cancellationToken);

        var weeklyCompleted = await _db.Set<Transaction>()
            .AsNoTracking()
            .CountAsync(t => t.Status == TransactionStatus.COMPLETED
                          && t.CompletedAt != null
                          && t.CompletedAt >= weeklyCutoff,
                cancellationToken);

        var summaryCards = new AdminDashboardSummaryCardsDto(
            ActiveTransactions: activeTransactions,
            PendingFlags: pendingFlags,
            DailyCompleted: dailyCompleted,
            WeeklyCompleted: weeklyCompleted);

        // Recent flags — newest-first across all statuses; spec just says
        // "Last 5 flags" so PENDING vs reviewed are mixed.
        var recentFlags = await _db.Set<FraudFlag>()
            .AsNoTracking()
            .OrderByDescending(f => f.CreatedAt)
            .ThenBy(f => f.Id)
            .Take(RecentFlagsLimit)
            .Select(f => new AdminDashboardRecentFlagDto(
                f.Id,
                f.TransactionId,
                f.Type.ToString(),
                f.Status.ToString(),
                f.CreatedAt))
            .ToListAsync(cancellationToken);


        return new AdminDashboardResponse(
            SummaryCards: summaryCards,
            RecentFlags: recentFlags);
    }
}
