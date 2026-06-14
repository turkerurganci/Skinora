using Microsoft.EntityFrameworkCore;
using Skinora.Disputes.Domain.Entities;
using Skinora.Fraud.Domain.Entities;
using Skinora.Notifications.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Models;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Admin;
using Skinora.Transactions.Domain.Calculations;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Services;

/// <summary>
/// Production <see cref="IAdminTransactionQueryService"/> — backs AD6 (list),
/// AD7 (detail) and AD16b (per-user list) per 07 §9.6 / §9.7 / §9.17.
/// Lives at the API composition root because AD7 detail composes data from
/// <c>Skinora.Notifications</c>, <c>Skinora.Disputes</c> and
/// <c>Skinora.Fraud</c> — modules that <c>Skinora.Transactions</c> cannot
/// reference without a project cycle.
/// </summary>
/// <remarks>
/// All queries are <c>AsNoTracking</c> projections. The detail endpoint
/// fans out into 4 small follow-up queries (history / blockchain rows /
/// notifications / disputes+flags) so each subset stays cheap and the
/// SQL Server plan cache remains stable. The page slice in <see cref="ListAsync"/>
/// projects only the visible columns, then resolves the seller/buyer
/// snapshots via a single dictionary join — same pattern as
/// <c>FraudFlagAdminQueryService</c>.
/// </remarks>
public sealed class AdminTransactionQueryService : IAdminTransactionQueryService
{
    private const int MinPage = 1;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    /// <summary>
    /// State set considered admin-cancellable per 07 §9.20. Excludes
    /// ITEM_DELIVERED, terminal CANCELLED_*/COMPLETED, and EMERGENCY_HOLD
    /// (the latter must be released first via AD19c). FLAGGED is included —
    /// admin may cancel directly without going through the flag review path.
    /// </summary>
    private static readonly HashSet<TransactionStatus> _adminCancellableStates =
    [
        TransactionStatus.CREATED,
        TransactionStatus.ACCEPTED,
        TransactionStatus.TRADE_OFFER_SENT_TO_SELLER,
        TransactionStatus.ITEM_ESCROWED,
        TransactionStatus.PAYMENT_RECEIVED,
        TransactionStatus.TRADE_OFFER_SENT_TO_BUYER,
        TransactionStatus.FLAGGED,
    ];

    /// <summary>
    /// Terminal states — mirrors <c>AdminDashboardService._terminalStates</c>
    /// so the <see cref="AdminTransactionStatusGroup.ACTIVE"/> bucket
    /// (= "not terminal") matches the AD1 dashboard active-transaction counter
    /// exactly (07 §9.6 / §9.1).
    /// </summary>
    private static readonly TransactionStatus[] _terminalStates =
    [
        TransactionStatus.COMPLETED,
        TransactionStatus.CANCELLED_TIMEOUT,
        TransactionStatus.CANCELLED_SELLER,
        TransactionStatus.CANCELLED_BUYER,
        TransactionStatus.CANCELLED_ADMIN,
    ];

    /// <summary>The four CANCELLED_* states behind the S15 "İptal" group (04 §8.4).</summary>
    private static readonly TransactionStatus[] _cancelledStates =
    [
        TransactionStatus.CANCELLED_TIMEOUT,
        TransactionStatus.CANCELLED_SELLER,
        TransactionStatus.CANCELLED_BUYER,
        TransactionStatus.CANCELLED_ADMIN,
    ];

    private readonly AppDbContext _db;

    public AdminTransactionQueryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<AdminTransactionListItemDto>> ListAsync(
        AdminTransactionListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var (safePage, safePageSize) = ClampPaging(query.Page, query.PageSize);
        var baseQuery = BuildFilteredQuery(query);

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var ordered = ApplyOrdering(baseQuery, query.SortBy, query.SortOrder);

        var pageRows = await ordered
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(t => new TxListProjection
            {
                Id = t.Id,
                ItemName = t.ItemName,
                ItemIconUrl = t.ItemIconUrl,
                Price = t.Price,
                Stablecoin = t.StablecoinType,
                Status = t.Status,
                SellerId = t.SellerId,
                BuyerId = t.BuyerId,
                CreatedAt = t.CreatedAt,
                CompletedAt = t.CompletedAt,
            })
            .ToListAsync(cancellationToken);

        var partyById = await ResolvePartiesAsync(pageRows, cancellationToken);
        var items = pageRows.Select(r => MapListItem(r, partyById)).ToList();

        return new PagedResult<AdminTransactionListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = safePage,
            PageSize = safePageSize,
        };
    }

    public async Task<PagedResult<AdminTransactionListItemDto>?> ListForUserAsync(
        string steamId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(steamId)) return null;

        var user = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.SteamId == steamId)
            .Select(u => new { u.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null) return null;

        var (safePage, safePageSize) = ClampPaging(page, pageSize);

        var baseQuery = _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => !t.IsDeleted && (t.SellerId == user.Id || t.BuyerId == user.Id));

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var ordered = baseQuery.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id);

        var pageRows = await ordered
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(t => new TxListProjection
            {
                Id = t.Id,
                ItemName = t.ItemName,
                ItemIconUrl = t.ItemIconUrl,
                Price = t.Price,
                Stablecoin = t.StablecoinType,
                Status = t.Status,
                SellerId = t.SellerId,
                BuyerId = t.BuyerId,
                CreatedAt = t.CreatedAt,
                CompletedAt = t.CompletedAt,
            })
            .ToListAsync(cancellationToken);

        var partyById = await ResolvePartiesAsync(pageRows, cancellationToken);
        var items = pageRows.Select(r => MapListItem(r, partyById)).ToList();

        return new PagedResult<AdminTransactionListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = safePage,
            PageSize = safePageSize,
        };
    }

    public async Task<AdminTransactionDetailDto?> GetDetailAsync(
        Guid transactionId, CancellationToken cancellationToken)
    {
        var tx = await _db.Set<Transaction>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted, cancellationToken);
        if (tx is null) return null;

        var sellerSnapshot = await LoadPartyAsync(tx.SellerId, cancellationToken);
        var buyerSnapshot = tx.BuyerId.HasValue
            ? await LoadPartyAsync(tx.BuyerId.Value, cancellationToken)
            : null;

        // statusHistory — order by CreatedAt then Id so concurrent state
        // changes within the same instant remain stable.
        var statusHistory = await _db.Set<TransactionHistory>()
            .AsNoTracking()
            .Where(h => h.TransactionId == tx.Id)
            .OrderBy(h => h.CreatedAt)
            .ThenBy(h => h.Id)
            .Select(h => new AdminTxStatusHistoryDto(
                h.PreviousStatus,
                h.NewStatus,
                h.CreatedAt,
                h.Trigger))
            .ToListAsync(cancellationToken);

        // Blockchain rows — fan out into payment / payout / refund sections.
        // Loaded once and bucketed in-memory so we avoid 3 round-trips.
        var chainRows = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .Where(b => b.TransactionId == tx.Id)
            .OrderBy(b => b.CreatedAt)
            .ThenBy(b => b.Id)
            .ToListAsync(cancellationToken);

        var paymentDetail = await BuildPaymentDetailAsync(tx.Id, chainRows, cancellationToken);
        var payoutDetail = BuildPayoutDetail(tx, chainRows);
        var refundDetail = BuildRefundDetail(chainRows);

        // Notifications — joined to deliveries so we can roll the channel set
        // up per notification (in-app is implicit; external channels appear
        // only when a NotificationDelivery row exists).
        var notifications = await BuildNotificationHistoryAsync(tx.Id, cancellationToken);

        // Disputes — open + closed (sorted oldest-first so the timeline reads
        // chronologically alongside statusHistory).
        var disputeHistory = await _db.Set<Dispute>()
            .AsNoTracking()
            .Where(d => d.TransactionId == tx.Id)
            .OrderBy(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .Select(d => new AdminTxDisputeDto(
                d.Id,
                d.Type.ToString(),
                d.Status.ToString(),
                d.SystemCheckResult,
                d.CreatedAt,
                d.ResolvedAt))
            .ToListAsync(cancellationToken);

        // Flags — newest-first matches AD2 default ordering.
        var flagHistory = await _db.Set<FraudFlag>()
            .AsNoTracking()
            .Where(f => f.TransactionId == tx.Id)
            .OrderByDescending(f => f.CreatedAt)
            .ThenBy(f => f.Id)
            .Select(f => new AdminTxFlagDto(
                f.Id,
                f.Type.ToString(),
                f.Status.ToString(),
                f.AdminNote,
                f.ReviewedAt))
            .ToListAsync(cancellationToken);

        var hasPendingFlag = flagHistory.Any(f => f.ReviewStatus == ReviewStatus.PENDING.ToString());
        var canCancel = !tx.IsOnHold && _adminCancellableStates.Contains(tx.Status);

        return new AdminTransactionDetailDto(
            Id: tx.Id,
            Status: tx.Status,
            ItemName: tx.ItemName,
            ItemImageUrl: tx.ItemIconUrl,
            ItemExterior: tx.ItemExterior,
            ItemInspectLink: tx.ItemInspectLink,
            Price: tx.Price,
            Stablecoin: tx.StablecoinType,
            CommissionRate: tx.CommissionRate,
            CommissionAmount: tx.CommissionAmount,
            TotalAmount: tx.TotalAmount,
            PaymentTimeoutMinutes: tx.PaymentTimeoutMinutes,
            Seller: sellerSnapshot ?? UnknownParty(),
            Buyer: buyerSnapshot,
            CreatedAt: tx.CreatedAt,
            AcceptedAt: tx.AcceptedAt,
            ItemEscrowedAt: tx.ItemEscrowedAt,
            PaymentReceivedAt: tx.PaymentReceivedAt,
            ItemDeliveredAt: tx.ItemDeliveredAt,
            CompletedAt: tx.CompletedAt,
            CancelledAt: tx.CancelledAt,
            CancelReason: tx.CancelReason,
            IsOnHold: tx.IsOnHold,
            EmergencyHoldAt: tx.EmergencyHoldAt,
            EmergencyHoldReason: tx.EmergencyHoldReason,
            StatusHistory: statusHistory,
            PaymentDetail: paymentDetail,
            SellerPayoutDetail: payoutDetail,
            RefundDetail: refundDetail,
            NotificationHistory: notifications,
            DisputeHistory: disputeHistory,
            FlagHistory: flagHistory,
            AdminActions: new AdminTxAdminActionsDto(
                CanApproveFlag: hasPendingFlag,
                CanRejectFlag: hasPendingFlag,
                CanCancel: canCancel));
    }

    // ---------- helpers ----------

    private IQueryable<Transaction> BuildFilteredQuery(AdminTransactionListQuery q)
    {
        var query = _db.Set<Transaction>().AsNoTracking().Where(t => !t.IsDeleted);

        if (q.Status.HasValue)
            query = query.Where(t => t.Status == q.Status.Value);
        if (q.StatusGroup.HasValue)
        {
            query = q.StatusGroup.Value switch
            {
                AdminTransactionStatusGroup.ACTIVE =>
                    query.Where(t => !_terminalStates.Contains(t.Status)),
                AdminTransactionStatusGroup.COMPLETED =>
                    query.Where(t => t.Status == TransactionStatus.COMPLETED),
                AdminTransactionStatusGroup.CANCELLED =>
                    query.Where(t => _cancelledStates.Contains(t.Status)),
                AdminTransactionStatusGroup.FLAGGED =>
                    query.Where(t => t.Status == TransactionStatus.FLAGGED),
                _ => query,
            };
        }
        if (q.Stablecoin.HasValue)
            query = query.Where(t => t.StablecoinType == q.Stablecoin.Value);
        if (q.DateFrom.HasValue)
            query = query.Where(t => t.CreatedAt >= q.DateFrom.Value);
        if (q.DateTo.HasValue)
            query = query.Where(t => t.CreatedAt <= q.DateTo.Value);
        if (q.MinAmount.HasValue)
            query = query.Where(t => t.Price >= q.MinAmount.Value);
        if (q.MaxAmount.HasValue)
            query = query.Where(t => t.Price <= q.MaxAmount.Value);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            // Search spans the cheap columns on Transaction itself plus the
            // seller/buyer denormalised tuples. The User-side LIKE survives
            // an inner subquery — SQL Server keeps both index scans cheap
            // because the candidate set is already filtered by the other
            // predicates above.
            var raw = q.Search.Trim();
            var escaped = EscapeLike(raw);
            var pattern = $"%{escaped}%";

            query = query.Where(t =>
                EF.Functions.Like(t.ItemName, pattern) ||
                _db.Set<User>().Any(u =>
                    (u.Id == t.SellerId || u.Id == t.BuyerId) &&
                    (EF.Functions.Like(u.SteamId, pattern) ||
                     EF.Functions.Like(u.SteamDisplayName, pattern))));
        }

        return query;
    }

    private static IOrderedQueryable<Transaction> ApplyOrdering(
        IQueryable<Transaction> query, string? sortBy, string? sortOrder)
    {
        var ascending = string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);

        // Default: newest first (07 §9.6 omits an order spec; admins want
        // the most recent transactions at the top of the list).
        return sortBy?.ToLowerInvariant() switch
        {
            "price" => ascending
                ? query.OrderBy(t => t.Price).ThenBy(t => t.Id)
                : query.OrderByDescending(t => t.Price).ThenBy(t => t.Id),
            "status" => ascending
                ? query.OrderBy(t => t.Status).ThenByDescending(t => t.CreatedAt)
                : query.OrderByDescending(t => t.Status).ThenByDescending(t => t.CreatedAt),
            _ => ascending
                ? query.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                : query.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id),
        };
    }

    private async Task<Dictionary<Guid, AdminTransactionPartyDto>> ResolvePartiesAsync(
        IReadOnlyList<TxListProjection> rows, CancellationToken cancellationToken)
    {
        var partyIds = rows
            .SelectMany(r => r.BuyerId.HasValue
                ? new[] { r.SellerId, r.BuyerId.Value }
                : new[] { r.SellerId })
            .Distinct()
            .ToList();

        if (partyIds.Count == 0) return [];

        var users = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => partyIds.Contains(u.Id))
            .Select(u => new { u.Id, u.SteamId, u.SteamDisplayName, u.SteamAvatarUrl })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(
            u => u.Id,
            u => new AdminTransactionPartyDto(u.SteamId, u.SteamDisplayName, u.SteamAvatarUrl));
    }

    private async Task<AdminTransactionPartyDto?> LoadPartyAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new AdminTransactionPartyDto(u.SteamId, u.SteamDisplayName, u.SteamAvatarUrl))
            .FirstOrDefaultAsync(cancellationToken);
        return user;
    }

    private static AdminTransactionListItemDto MapListItem(
        TxListProjection row, IReadOnlyDictionary<Guid, AdminTransactionPartyDto> partyById)
    {
        var seller = partyById.TryGetValue(row.SellerId, out var s) ? s : UnknownParty();
        AdminTransactionPartyDto? buyer = row.BuyerId.HasValue
            && partyById.TryGetValue(row.BuyerId.Value, out var b) ? b : null;

        return new AdminTransactionListItemDto(
            Id: row.Id,
            ItemName: row.ItemName,
            ItemImageUrl: row.ItemIconUrl,
            Price: row.Price,
            Stablecoin: row.Stablecoin,
            Status: row.Status,
            Seller: seller,
            Buyer: buyer,
            CreatedAt: row.CreatedAt,
            CompletedAt: row.CompletedAt);
    }

    /// <summary>
    /// Stable placeholder used when the seller/buyer row has been hard-deleted
    /// or anonymized after the transaction landed. Matches the "Deleted User"
    /// convention from 02 §19 (account anonymization).
    /// </summary>
    private static AdminTransactionPartyDto UnknownParty() =>
        new(SteamId: string.Empty, DisplayName: "Deleted User", AvatarUrl: null);

    private async Task<AdminTxPaymentDetailDto?> BuildPaymentDetailAsync(
        Guid transactionId,
        IReadOnlyList<BlockchainTransaction> chainRows,
        CancellationToken cancellationToken)
    {
        var payment = chainRows
            .Where(b => b.Type == BlockchainTransactionType.BUYER_PAYMENT)
            .OrderByDescending(b => b.ConfirmedAt ?? b.CreatedAt)
            .FirstOrDefault();

        var paymentAddress = await _db.Set<PaymentAddress>()
            .AsNoTracking()
            .Where(p => p.TransactionId == transactionId && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.Address)
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null && string.IsNullOrEmpty(paymentAddress))
            return null;

        return new AdminTxPaymentDetailDto(
            PaymentAddress: paymentAddress,
            ReceivedAmount: payment?.Amount ?? 0m,
            ReceivedTxHash: payment?.TxHash,
            BlockConfirmations: payment?.ConfirmationCount ?? 0,
            ConfirmedAt: payment?.ConfirmedAt);
    }

    private static AdminTxSellerPayoutDetailDto? BuildPayoutDetail(
        Transaction tx, IReadOnlyList<BlockchainTransaction> chainRows)
    {
        var payout = chainRows
            .Where(b => b.Type == BlockchainTransactionType.SELLER_PAYOUT)
            .OrderByDescending(b => b.ConfirmedAt ?? b.CreatedAt)
            .FirstOrDefault();

        if (payout is null) return null;

        // WP1 — the seller-send gas estimate used at payout time is snapshotted
        // onto BlockchainTransaction.GasFee, so the split is reconstructable
        // from stored data (07 §7.5). Legacy rows with no snapshot report the
        // commission share as 0.
        var split = FinancialCalculator.ReconstructSellerPayoutSplit(
            tx.Price, payout.Amount, payout.GasFee);
        return new AdminTxSellerPayoutDetailDto(
            GrossAmount: tx.Price,
            Commission: tx.CommissionAmount,
            GasFee: payout.GasFee,
            GasFeeFromCommission: split.GasFeeFromCommission,
            GasFeeFromSeller: split.GasFeeFromSeller,
            NetAmount: payout.Amount,
            TxHash: payout.TxHash,
            SentAt: payout.ConfirmedAt ?? payout.CreatedAt);
    }

    private static AdminTxRefundDetailDto? BuildRefundDetail(
        IReadOnlyList<BlockchainTransaction> chainRows)
    {
        var refund = chainRows
            .Where(b => b.Type is BlockchainTransactionType.BUYER_REFUND
                or BlockchainTransactionType.EXCESS_REFUND
                or BlockchainTransactionType.WRONG_TOKEN_REFUND
                or BlockchainTransactionType.LATE_PAYMENT_REFUND
                or BlockchainTransactionType.INCORRECT_AMOUNT_REFUND)
            .OrderByDescending(b => b.ConfirmedAt ?? b.CreatedAt)
            .FirstOrDefault();

        if (refund is null) return null;

        var gasFee = refund.GasFee ?? 0m;
        return new AdminTxRefundDetailDto(
            OriginalAmount: refund.Amount + gasFee,
            GasFee: refund.GasFee,
            NetRefundAmount: refund.Amount,
            RefundAddress: refund.ToAddress,
            TxHash: refund.TxHash,
            RefundedAt: refund.ConfirmedAt ?? refund.CreatedAt);
    }

    private async Task<IReadOnlyList<AdminTxNotificationDto>> BuildNotificationHistoryAsync(
        Guid transactionId, CancellationToken cancellationToken)
    {
        var notifications = await _db.Set<Notification>()
            .AsNoTracking()
            .Where(n => n.TransactionId == transactionId && !n.IsDeleted)
            .OrderBy(n => n.CreatedAt)
            .ThenBy(n => n.Id)
            .Select(n => new
            {
                n.Id,
                n.UserId,
                n.Type,
                n.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        if (notifications.Count == 0) return [];

        var notificationIds = notifications.Select(n => n.Id).ToList();
        var deliveryRows = await _db.Set<NotificationDelivery>()
            .AsNoTracking()
            .Where(d => notificationIds.Contains(d.NotificationId)
                     && d.Status == DeliveryStatus.SENT)
            .Select(d => new { d.NotificationId, d.Channel })
            .ToListAsync(cancellationToken);

        var channelsByNotificationId = deliveryRows
            .GroupBy(d => d.NotificationId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Channel.ToString())
                      .Distinct()
                      .OrderBy(c => c)
                      .ToList());

        var userIds = notifications.Select(n => n.UserId).Distinct().ToList();
        var displayNamesById = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.SteamDisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.SteamDisplayName, cancellationToken);

        return notifications.Select(n =>
        {
            var channels = channelsByNotificationId.TryGetValue(n.Id, out var ch)
                ? AddInApp(ch)
                : (IReadOnlyList<string>)[InAppChannel];

            var recipient = displayNamesById.TryGetValue(n.UserId, out var name)
                ? name
                : n.UserId.ToString();

            return new AdminTxNotificationDto(
                Type: n.Type.ToString(),
                Recipient: recipient,
                Channels: channels,
                SentAt: n.CreatedAt);
        }).ToList();
    }

    /// <summary>
    /// In-app delivery is implicit (the row in <c>Notification</c> itself is
    /// the delivery) — splice it onto the front of the channel set so the
    /// admin always sees IN_APP listed alongside any external fan-out.
    /// </summary>
    private const string InAppChannel = "IN_APP";

    private static IReadOnlyList<string> AddInApp(IReadOnlyList<string> channels)
    {
        var combined = new List<string>(channels.Count + 1) { InAppChannel };
        combined.AddRange(channels);
        return combined;
    }

    private static (int Page, int PageSize) ClampPaging(int page, int pageSize)
    {
        var safePage = page < MinPage ? MinPage : page;
        var safePageSize = pageSize < MinPageSize
            ? DefaultPageSize
            : pageSize > MaxPageSize ? MaxPageSize : pageSize;
        return (safePage, safePageSize);
    }

    /// <summary>
    /// SQL Server <c>LIKE</c> escape using bracket-wrapping — works without
    /// an <c>ESCAPE</c> clause (which the standard <c>EF.Functions.Like</c>
    /// 2-arg overload does not emit). Order matters: <c>[</c> must be
    /// rewritten first because the subsequent rewrites introduce literal
    /// brackets we do not want to re-process.
    /// </summary>
    private static string EscapeLike(string value)
    {
        return value
            .Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
    }

    private sealed class TxListProjection
    {
        public Guid Id { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public string? ItemIconUrl { get; init; }
        public decimal Price { get; init; }
        public StablecoinType Stablecoin { get; init; }
        public TransactionStatus Status { get; init; }
        public Guid SellerId { get; init; }
        public Guid? BuyerId { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
    }
}
