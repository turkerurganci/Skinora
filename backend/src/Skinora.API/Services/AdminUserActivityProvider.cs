using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Admin.Application.Users;
using Skinora.Disputes.Domain.Entities;
using Skinora.Fraud.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Services;

/// <summary>
/// Production <see cref="IAdminUserActivityProvider"/> — backs the
/// transaction / flag / dispute / counterparty sections of AD16 user detail
/// (07 §9.16). Lives at the API composition root because it reads across
/// <c>Skinora.Transactions</c>, <c>Skinora.Fraud</c> and
/// <c>Skinora.Disputes</c> (modules <c>Skinora.Admin</c> cannot reference) —
/// same rationale as <c>AdminTransactionQueryService</c>.
/// </summary>
/// <remarks>
/// All queries are <c>AsNoTracking</c> and ride the soft-delete query filters.
/// Per-user aggregation is intentionally fetched as small projections and
/// folded in memory: the counterparty of a row is a conditional (the *other*
/// party), which EF cannot express as a single GROUP BY, and enum→string
/// happens client-side to stay provider-agnostic (SQLite stores enums as int).
/// </remarks>
public sealed class AdminUserActivityProvider : IAdminUserActivityProvider
{
    private const int MaxCounterparties = 10;

    /// <summary>
    /// Terminal states — mirrors <c>AdminTransactionQueryService._terminalStates</c>
    /// / <c>AdminDashboardService._terminalStates</c> so the "active" count here
    /// matches the AD1 dashboard active-transaction counter and the AD19d
    /// hold-by-user predicate exactly (07 §9.16 / §9.1 / §9.22a).
    /// </summary>
    private static readonly TransactionStatus[] _terminalStates =
    [
        TransactionStatus.COMPLETED,
        TransactionStatus.CANCELLED_TIMEOUT,
        TransactionStatus.CANCELLED_SELLER,
        TransactionStatus.CANCELLED_BUYER,
        TransactionStatus.CANCELLED_ADMIN,
    ];

    /// <summary>The four CANCELLED_* states behind the S20 "İptal" stat (04 §8.9.2).</summary>
    private static readonly TransactionStatus[] _cancelledStates =
    [
        TransactionStatus.CANCELLED_TIMEOUT,
        TransactionStatus.CANCELLED_SELLER,
        TransactionStatus.CANCELLED_BUYER,
        TransactionStatus.CANCELLED_ADMIN,
    ];

    private readonly AppDbContext _db;

    public AdminUserActivityProvider(AppDbContext db)
    {
        _db = db;
    }

    public async Task<AdminUserActivity> GetAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        // --- Transactions: stats + badge signals + counterparties ---
        // Single projection pass over every transaction the user is party to
        // (buyer OR seller). Soft-deleted rows are excluded by the query filter.
        var txRows = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.SellerId == userId || t.BuyerId == userId)
            .Select(t => new TxRow
            {
                Status = t.Status,
                TotalAmount = t.TotalAmount,
                IsOnHold = t.IsOnHold,
                CreatedAt = t.CreatedAt,
                SellerId = t.SellerId,
                BuyerId = t.BuyerId,
            })
            .ToListAsync(cancellationToken);

        var totalTransactions = txRows.Count;
        var completed = txRows.Count(r => r.Status == TransactionStatus.COMPLETED);
        var cancelled = txRows.Count(r => _cancelledStates.Contains(r.Status));
        var flagged = txRows.Count(r => r.Status == TransactionStatus.FLAGGED);
        var activeCount = txRows.Count(r => !_terminalStates.Contains(r.Status));
        var hasOnHold = txRows.Any(r => r.IsOnHold);

        // Realized volume — sum of COMPLETED transaction amounts, serialized as
        // an invariant 2-dp string (06 §8 money rounding). Null when the user
        // has no completed transaction yet (distinct from "0.00").
        var completedRows = txRows
            .Where(r => r.Status == TransactionStatus.COMPLETED)
            .ToList();
        string? totalVolume = completedRows.Count > 0
            ? completedRows.Sum(r => r.TotalAmount).ToString("0.00", CultureInfo.InvariantCulture)
            : null;

        DateTime? lastTransactionAt = txRows.Count > 0
            ? txRows.Max(r => r.CreatedAt)
            : null;

        var frequentCounterparties =
            await BuildCounterpartiesAsync(userId, txRows, cancellationToken);

        // --- Flag history (04 §8.9.5) — every flag naming this user. Account-
        // level rows carry a null transactionId (06 §3.12). Enum→string in
        // memory after the projection materializes.
        var flagRows = await _db.Set<FraudFlag>()
            .AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new { f.Id, f.Type, f.TransactionId, f.Status, f.CreatedAt })
            .ToListAsync(cancellationToken);
        var flagHistory = flagRows
            .Select(f => new AdminUserFlagEntryDto(
                f.Id, f.Type.ToString(), f.TransactionId, f.Status.ToString(), f.CreatedAt))
            .ToList();

        // --- Dispute history (04 §8.9.6) — disputes on transactions where the
        // user is a party. Disputes are buyer-opened, but the seller is equally
        // a party, so we join through the transaction rather than filtering on
        // OpenedByUserId.
        var disputeRows = await (
            from d in _db.Set<Dispute>().AsNoTracking()
            join t in _db.Set<Transaction>().AsNoTracking() on d.TransactionId equals t.Id
            where t.SellerId == userId || t.BuyerId == userId
            orderby d.CreatedAt descending
            select new { d.Id, d.Type, d.TransactionId, d.Status, d.CreatedAt })
            .ToListAsync(cancellationToken);
        var disputeHistory = disputeRows
            .Select(d => new AdminUserDisputeEntryDto(
                d.Id, d.Type.ToString(), d.TransactionId, d.Status.ToString(), d.CreatedAt))
            .ToList();

        return new AdminUserActivity(
            TotalTransactions: totalTransactions,
            CompletedTransactions: completed,
            CancelledTransactions: cancelled,
            FlaggedTransactions: flagged,
            TotalVolume: totalVolume,
            LastTransactionAt: lastTransactionAt,
            ActiveTransactionCount: activeCount,
            HasTransactionOnHold: hasOnHold,
            FlagHistory: flagHistory,
            DisputeHistory: disputeHistory,
            FrequentCounterparties: frequentCounterparties);
    }

    /// <summary>
    /// 04 §8.9.7 frequent counterparties (wash-trading signal). The counterparty
    /// is the *other* party on each row; rows without a buyer yet are skipped.
    /// Top <see cref="MaxCounterparties"/> by shared-transaction count, names
    /// resolved via a single dictionary lookup.
    /// </summary>
    private async Task<IReadOnlyList<AdminUserCounterpartyDto>> BuildCounterpartiesAsync(
        Guid userId, IReadOnlyList<TxRow> txRows, CancellationToken cancellationToken)
    {
        var groups = txRows
            .Select(r => new
            {
                CounterpartyId = r.SellerId == userId ? r.BuyerId : (Guid?)r.SellerId,
                r.CreatedAt,
            })
            .Where(x => x.CounterpartyId.HasValue)
            .GroupBy(x => x.CounterpartyId!.Value)
            .Select(g => new
            {
                CounterpartyId = g.Key,
                Count = g.Count(),
                LastAt = g.Max(x => x.CreatedAt),
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.LastAt)
            .Take(MaxCounterparties)
            .ToList();

        if (groups.Count == 0) return [];

        var ids = groups.Select(g => g.CounterpartyId).ToList();
        var users = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.SteamId, u.SteamDisplayName })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        // A counterparty anonymized/soft-deleted after the trade landed is dropped
        // by the global User query filter (!IsDeleted), so the lookup misses it.
        // Fall back to the "Deleted User" placeholder with an empty SteamId — the
        // same convention as the sibling AD7 AdminTransactionQueryService.UnknownParty()
        // (02 §19). The S20 §8.9.7 cell renders an empty-SteamId counterparty as plain
        // text rather than a broken link.
        return groups
            .Select(g =>
            {
                users.TryGetValue(g.CounterpartyId, out var info);
                return new AdminUserCounterpartyDto(
                    SteamId: info?.SteamId ?? string.Empty,
                    DisplayName: info?.SteamDisplayName ?? "Deleted User",
                    TransactionCount: g.Count,
                    LastTransactionAt: g.LastAt);
            })
            .ToList();
    }

    private sealed class TxRow
    {
        public TransactionStatus Status { get; init; }
        public decimal TotalAmount { get; init; }
        public bool IsOnHold { get; init; }
        public DateTime CreatedAt { get; init; }
        public Guid SellerId { get; init; }
        public Guid? BuyerId { get; init; }
    }
}
