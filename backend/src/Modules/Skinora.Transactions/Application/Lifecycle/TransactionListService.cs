using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Models;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Lifecycle;

/// <summary>
/// Production <see cref="ITransactionListService"/> — backs T1
/// <c>GET /transactions</c> (07 §7.1, T83a). All queries are
/// <c>AsNoTracking</c>; the page slice projects only the columns the list
/// row needs, then a single follow-up dictionary join resolves the
/// counterparty snapshot. Same shape as
/// <c>AdminTransactionQueryService.ListAsync</c>, scoped to the caller's
/// own party rows.
/// </summary>
public sealed class TransactionListService : ITransactionListService
{
    private const int MinPage = 1;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    // Active tab includes FLAGGED so the seller sees price-deviation rows
    // pending admin review alongside their working transactions (07 §7.1).
    private static readonly TransactionStatus[] _activeStatuses =
    [
        TransactionStatus.CREATED,
        TransactionStatus.ACCEPTED,
        TransactionStatus.SELLER_CONFIRMED,
        TransactionStatus.PAYMENT_RECEIVED,
        TransactionStatus.ITEM_DELIVERED,
        TransactionStatus.FLAGGED,
    ];

    private static readonly TransactionStatus[] _cancelledStatuses =
    [
        TransactionStatus.CANCELLED_TIMEOUT,
        TransactionStatus.CANCELLED_SELLER,
        TransactionStatus.CANCELLED_BUYER,
        TransactionStatus.CANCELLED_ADMIN,
        // WP5 — buyer-favor dispute refund; surfaced in the user "Cancelled" tab.
        TransactionStatus.REFUNDED,
    ];

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public TransactionListService(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<PagedResult<TransactionListItemDto>> ListAsync(
        Guid callerId, TransactionListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var (safePage, safePageSize) = ClampPaging(query.Page, query.PageSize);
        var statuses = ResolveStatusFilter(query.Tab);

        var baseQuery = _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => !t.IsDeleted
                        && (t.SellerId == callerId || t.BuyerId == callerId)
                        && statuses.Contains(t.Status));

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        // Project to a row-shaped intermediate so the counterparty IDs can be
        // batched into a single User dictionary lookup. CreatedAt DESC matches
        // the 11 §T83a "newest first" order; Id is a deterministic tiebreaker.
        var pageRows = await baseQuery
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(t => new TxListProjection
            {
                Id = t.Id,
                ItemName = t.ItemName,
                ItemIconUrl = t.ItemIconUrl,
                Status = t.Status,
                Price = t.Price,
                Stablecoin = t.StablecoinType,
                SellerId = t.SellerId,
                BuyerId = t.BuyerId,
                AcceptDeadline = t.AcceptDeadline,
                SellerConfirmDeadline = t.SellerConfirmDeadline,
                PaymentDeadline = t.PaymentDeadline,
                DeliveryDeadline = t.DeliveryDeadline,
                IsOnHold = t.IsOnHold,
                TimeoutFrozenAt = t.TimeoutFrozenAt,
                TimeoutRemainingSeconds = t.TimeoutRemainingSeconds,
                CreatedAt = t.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var counterpartyById = await LoadCounterpartiesAsync(pageRows, callerId, cancellationToken);
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        // WP12 (T83a) — resolve the warning threshold once per page (not per
        // row) from the admin-tunable timeout_warning_ratio setting; fallback 75.
        var warningPercent = await TimeoutWarningThreshold.ReadPercentAsync(_db, cancellationToken);
        var items = pageRows
            .Select(r => MapRow(r, callerId, counterpartyById, nowUtc, warningPercent))
            .ToList();

        return new PagedResult<TransactionListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = safePage,
            PageSize = safePageSize,
        };
    }

    private async Task<Dictionary<Guid, TransactionListCounterpartyDto>> LoadCounterpartiesAsync(
        IReadOnlyCollection<TxListProjection> rows, Guid callerId, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];

        var counterpartyIds = rows
            .Select(r => r.SellerId == callerId ? r.BuyerId : (Guid?)r.SellerId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (counterpartyIds.Count == 0) return [];

        return await _db.Set<User>()
            .AsNoTracking()
            .Where(u => counterpartyIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.SteamId,
                u.SteamDisplayName,
                u.SteamAvatarUrl,
            })
            .ToDictionaryAsync(
                u => u.Id,
                u => new TransactionListCounterpartyDto(u.SteamId, u.SteamDisplayName, u.SteamAvatarUrl),
                cancellationToken);
    }

    private static TransactionListItemDto MapRow(
        TxListProjection row,
        Guid callerId,
        IReadOnlyDictionary<Guid, TransactionListCounterpartyDto> counterpartyById,
        DateTime nowUtc,
        int warningPercent)
    {
        var userRole = row.SellerId == callerId ? "seller" : "buyer";

        var counterpartyId = row.SellerId == callerId ? row.BuyerId : (Guid?)row.SellerId;
        TransactionListCounterpartyDto? counterparty = null;
        if (counterpartyId.HasValue && counterpartyById.TryGetValue(counterpartyId.Value, out var party))
            counterparty = party;

        return new TransactionListItemDto(
            Id: row.Id,
            ItemName: row.ItemName,
            ItemImageUrl: row.ItemIconUrl,
            Status: ProjectStatus(row),
            Price: row.Price.ToString("F2", CultureInfo.InvariantCulture),
            Stablecoin: row.Stablecoin,
            Counterparty: counterparty,
            UserRole: userRole,
            ActiveTimeout: BuildActiveTimeout(row, nowUtc, warningPercent),
            CreatedAt: row.CreatedAt);
    }

    /// <summary>
    /// 07 §7.1 status projection. EMERGENCY_HOLD is not a
    /// <see cref="TransactionStatus"/> value — it is the response shape when
    /// <c>IsOnHold=true</c> overlays any active state (06 §3.5, 05 §4.5).
    /// </summary>
    private static string ProjectStatus(TxListProjection row) =>
        row.IsOnHold ? "EMERGENCY_HOLD" : row.Status.ToString();

    /// <summary>
    /// Active timeout block — mirrors the 06 §3.5 state→active-deadline
    /// matrix used by <c>TimeoutFreezeService</c> and
    /// <c>TransactionDetailService</c>. Terminal + matrix-blank states
    /// return null. Frozen rows surface the persisted remainder; live rows
    /// compute from (deadline − now).
    /// </summary>
    private static TransactionListActiveTimeoutDto? BuildActiveTimeout(TxListProjection row, DateTime nowUtc, int warningPercent)
    {
        var (type, expiresAt) = row.Status switch
        {
            TransactionStatus.CREATED when row.AcceptDeadline.HasValue
                => ("accept", row.AcceptDeadline.Value),
            TransactionStatus.ACCEPTED when row.SellerConfirmDeadline.HasValue
                => ("seller_confirm", row.SellerConfirmDeadline.Value),
            TransactionStatus.SELLER_CONFIRMED when row.PaymentDeadline.HasValue
                => ("payment", row.PaymentDeadline.Value),
            TransactionStatus.PAYMENT_RECEIVED when row.DeliveryDeadline.HasValue
                => ("delivery", row.DeliveryDeadline.Value),
            _ => (string.Empty, default(DateTime)),
        };

        if (string.IsNullOrEmpty(type)) return null;

        var remaining = row.TimeoutFrozenAt.HasValue
            ? row.TimeoutRemainingSeconds ?? 0
            : Math.Max(0, (int)Math.Floor((expiresAt - nowUtc).TotalSeconds));

        return new TransactionListActiveTimeoutDto(
            Type: type,
            ExpiresAt: expiresAt,
            RemainingSeconds: remaining,
            WarningThresholdPercent: warningPercent);
    }

    private static IReadOnlyList<TransactionStatus> ResolveStatusFilter(TransactionListTab tab) => tab switch
    {
        TransactionListTab.Active => _activeStatuses,
        TransactionListTab.Completed => [TransactionStatus.COMPLETED],
        TransactionListTab.Cancelled => _cancelledStatuses,
        _ => _activeStatuses,
    };

    private static (int Page, int PageSize) ClampPaging(int page, int pageSize)
    {
        var safePage = page < MinPage ? MinPage : page;
        var safePageSize = pageSize < MinPageSize ? DefaultPageSize
            : pageSize > MaxPageSize ? MaxPageSize
            : pageSize;
        return (safePage, safePageSize);
    }

    private sealed class TxListProjection
    {
        public Guid Id { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public string? ItemIconUrl { get; init; }
        public TransactionStatus Status { get; init; }
        public decimal Price { get; init; }
        public StablecoinType Stablecoin { get; init; }
        public Guid SellerId { get; init; }
        public Guid? BuyerId { get; init; }
        public DateTime? AcceptDeadline { get; init; }
        public DateTime? SellerConfirmDeadline { get; init; }
        public DateTime? PaymentDeadline { get; init; }
        public DateTime? DeliveryDeadline { get; init; }
        public bool IsOnHold { get; init; }
        public DateTime? TimeoutFrozenAt { get; init; }
        public int? TimeoutRemainingSeconds { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
