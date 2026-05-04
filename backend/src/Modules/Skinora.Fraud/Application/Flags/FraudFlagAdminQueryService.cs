using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Fraud.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Application.Profiles;
using Skinora.Users.Application.Reputation;
using Skinora.Users.Domain.Entities;

namespace Skinora.Fraud.Application.Flags;

/// <inheritdoc cref="IFraudFlagAdminQueryService"/>
public sealed class FraudFlagAdminQueryService : IFraudFlagAdminQueryService
{
    private const int MinPage = 1;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    /// <summary>
    /// JSON options used to parse <see cref="FraudFlag.Details"/> into the
    /// type-specific <c>flagDetail</c> payloads. <c>PropertyNameCaseInsensitive</c>
    /// is set so signal generators can persist either casing without being
    /// coupled to a single producer.
    /// </summary>
    private static readonly JsonSerializerOptions DetailJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly AppDbContext _db;
    private readonly IReputationScoreCalculator _reputation;
    private readonly TimeProvider _clock;

    public FraudFlagAdminQueryService(
        AppDbContext db,
        IReputationScoreCalculator reputation,
        TimeProvider clock)
    {
        _db = db;
        _reputation = reputation;
        _clock = clock;
    }

    public async Task<FraudFlagListResponse> ListAsync(
        FraudFlagListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var safePage = query.Page < MinPage ? MinPage : query.Page;
        var safePageSize = query.PageSize < MinPageSize
            ? DefaultPageSize
            : query.PageSize > MaxPageSize ? MaxPageSize : query.PageSize;

        var baseQuery = _db.Set<FraudFlag>().AsNoTracking();

        if (query.Type.HasValue)
            baseQuery = baseQuery.Where(f => f.Type == query.Type.Value);
        if (query.ReviewStatus.HasValue)
            baseQuery = baseQuery.Where(f => f.Status == query.ReviewStatus.Value);
        if (query.DateFrom.HasValue)
            baseQuery = baseQuery.Where(f => f.CreatedAt >= query.DateFrom.Value);
        if (query.DateTo.HasValue)
            baseQuery = baseQuery.Where(f => f.CreatedAt <= query.DateTo.Value);

        var orderedQuery = ApplyOrdering(baseQuery, query.SortBy, query.SortOrder);

        // Single page round-trip + a separate count + a separate badge. Three
        // queries instead of one composite to keep each plan stable in SQL
        // Server (the seller / transaction joins on the page slice are a JOIN
        // on at most pageSize rows; the count + pending counters scan only the
        // FraudFlag table).
        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var pendingCount = await _db.Set<FraudFlag>()
            .AsNoTracking()
            .CountAsync(f => f.Status == ReviewStatus.PENDING, cancellationToken);

        var pageRows = await orderedQuery
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(f => new
            {
                f.Id,
                f.TransactionId,
                f.Scope,
                f.Type,
                f.Status,
                f.UserId,
                f.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var transactionIds = pageRows
            .Where(r => r.TransactionId.HasValue)
            .Select(r => r.TransactionId!.Value)
            .Distinct()
            .ToList();

        var sellerIds = pageRows.Select(r => r.UserId).Distinct().ToList();

        var transactionsById = transactionIds.Count == 0
            ? new Dictionary<Guid, TransactionListProjection>()
            : await _db.Set<Transaction>()
                .AsNoTracking()
                .Where(t => transactionIds.Contains(t.Id))
                .Select(t => new TransactionListProjection
                {
                    Id = t.Id,
                    Status = t.Status,
                    ItemName = t.ItemName,
                    ItemIconUrl = t.ItemIconUrl,
                    Price = t.Price,
                    Stablecoin = t.StablecoinType,
                    PaymentTimeoutMinutes = t.PaymentTimeoutMinutes,
                    CreatedAt = t.CreatedAt,
                    MarketPrice = t.MarketPriceAtCreation,
                })
                .ToDictionaryAsync(t => t.Id, cancellationToken);

        var sellersById = sellerIds.Count == 0
            ? new Dictionary<Guid, UserPartyProjection>()
            : await _db.Set<User>()
                .AsNoTracking()
                .Where(u => sellerIds.Contains(u.Id))
                .Select(u => new UserPartyProjection
                {
                    Id = u.Id,
                    SteamId = u.SteamId,
                    DisplayName = u.SteamDisplayName,
                    AvatarUrl = u.SteamAvatarUrl,
                })
                .ToDictionaryAsync(u => u.Id, cancellationToken);

        var items = pageRows
            .Select(r =>
            {
                TransactionListProjection? tx = null;
                if (r.TransactionId.HasValue && transactionsById.TryGetValue(r.TransactionId.Value, out var hit))
                    tx = hit;

                FlagPartyDto? seller = null;
                if (sellersById.TryGetValue(r.UserId, out var sellerHit))
                {
                    seller = new FlagPartyDto(
                        SteamId: sellerHit.SteamId,
                        DisplayName: sellerHit.DisplayName,
                        AvatarUrl: sellerHit.AvatarUrl);
                }

                return new FraudFlagListItemDto(
                    Id: r.Id,
                    TransactionId: r.TransactionId,
                    Scope: r.Scope,
                    Type: r.Type,
                    ReviewStatus: r.Status,
                    Seller: seller,
                    ItemName: tx?.ItemName,
                    Price: tx?.Price,
                    Stablecoin: tx?.Stablecoin,
                    MarketPrice: tx?.MarketPrice,
                    CreatedAt: r.CreatedAt);
            })
            .ToList();

        return new FraudFlagListResponse(
            Items: items,
            TotalCount: totalCount,
            Page: safePage,
            PageSize: safePageSize,
            PendingCount: pendingCount);
    }

    public async Task<FraudFlagDetailDto?> GetDetailAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var flag = await _db.Set<FraudFlag>()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (flag is null) return null;

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        FlagTransactionDto? txDto = null;
        FlagPartyDetailDto? buyerDto = null;
        if (flag.TransactionId.HasValue)
        {
            var tx = await _db.Set<Transaction>()
                .AsNoTracking()
                .Where(t => t.Id == flag.TransactionId.Value)
                .Select(t => new
                {
                    t.Id,
                    t.Status,
                    t.ItemName,
                    t.ItemIconUrl,
                    t.Price,
                    t.StablecoinType,
                    t.PaymentTimeoutMinutes,
                    t.CreatedAt,
                    t.BuyerId,
                })
                .FirstOrDefaultAsync(cancellationToken);
            if (tx is not null)
            {
                txDto = new FlagTransactionDto(
                    Id: tx.Id,
                    Status: tx.Status,
                    ItemName: tx.ItemName,
                    ItemImageUrl: tx.ItemIconUrl,
                    Price: tx.Price,
                    Stablecoin: tx.StablecoinType,
                    PaymentTimeoutHours: tx.PaymentTimeoutMinutes / 60,
                    CreatedAt: tx.CreatedAt);

                if (tx.BuyerId.HasValue)
                {
                    var buyer = await _db.Set<User>()
                        .AsNoTracking()
                        .Where(u => u.Id == tx.BuyerId.Value)
                        .Select(u => new UserPartyProjection
                        {
                            Id = u.Id,
                            SteamId = u.SteamId,
                            DisplayName = u.SteamDisplayName,
                            AvatarUrl = u.SteamAvatarUrl,
                            CreatedAt = u.CreatedAt,
                            CompletedTransactionCount = u.CompletedTransactionCount,
                            SuccessfulTransactionRate = u.SuccessfulTransactionRate,
                        })
                        .FirstOrDefaultAsync(cancellationToken);
                    if (buyer is not null)
                        buyerDto = await BuildPartyDetailAsync(buyer, nowUtc, cancellationToken);
                }
            }
        }

        var sellerProjection = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == flag.UserId)
            .Select(u => new UserPartyProjection
            {
                Id = u.Id,
                SteamId = u.SteamId,
                DisplayName = u.SteamDisplayName,
                AvatarUrl = u.SteamAvatarUrl,
                CreatedAt = u.CreatedAt,
                CompletedTransactionCount = u.CompletedTransactionCount,
                SuccessfulTransactionRate = u.SuccessfulTransactionRate,
            })
            .FirstOrDefaultAsync(cancellationToken);

        var sellerDto = sellerProjection is null
            ? null
            : await BuildPartyDetailAsync(sellerProjection, nowUtc, cancellationToken);

        // historicalTransactionCount — completed transactions where the
        // flagged user was the seller. Cancelled / in-flight transactions
        // intentionally do not count (07 §9.3 wording matches the trust
        // signal admins use to gauge user history).
        var historicalCount = await _db.Set<Transaction>()
            .AsNoTracking()
            .CountAsync(t => t.SellerId == flag.UserId
                          && t.Status == TransactionStatus.COMPLETED,
                cancellationToken);

        var detailPayload = ParseDetail(flag);

        return new FraudFlagDetailDto(
            Id: flag.Id,
            Scope: flag.Scope,
            Type: flag.Type,
            ReviewStatus: flag.Status,
            CreatedAt: flag.CreatedAt,
            FlagDetail: detailPayload,
            Transaction: txDto,
            Seller: sellerDto,
            Buyer: buyerDto,
            HistoricalTransactionCount: historicalCount,
            ReviewedByAdminId: flag.ReviewedByAdminId,
            ReviewedAt: flag.ReviewedAt,
            AdminNote: flag.AdminNote);
    }

    private async Task<FlagPartyDetailDto> BuildPartyDetailAsync(
        UserPartyProjection projection, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var reputation = await _reputation.ComputeAsync(
            projection.CompletedTransactionCount,
            projection.SuccessfulTransactionRate,
            projection.CreatedAt,
            nowUtc,
            cancellationToken);

        return new FlagPartyDetailDto(
            SteamId: projection.SteamId,
            DisplayName: projection.DisplayName,
            AvatarUrl: projection.AvatarUrl,
            ReputationScore: reputation,
            CompletedTransactionCount: projection.CompletedTransactionCount,
            AccountAge: AccountAgeFormatter.Format(projection.CreatedAt, nowUtc));
    }

    private static IOrderedQueryable<FraudFlag> ApplyOrdering(
        IQueryable<FraudFlag> query, string? sortBy, string? sortOrder)
    {
        // Default order: newest first (07 §9.2 — flags are reviewed in arrival order).
        // Caller can ask for asc/desc; unrecognised sortBy falls back to CreatedAt desc.
        var ascending = string.Equals(sortOrder, "asc", StringComparison.OrdinalIgnoreCase);

        return sortBy?.ToLowerInvariant() switch
        {
            "type" => ascending
                ? query.OrderBy(f => f.Type).ThenByDescending(f => f.CreatedAt)
                : query.OrderByDescending(f => f.Type).ThenByDescending(f => f.CreatedAt),
            "reviewstatus" => ascending
                ? query.OrderBy(f => f.Status).ThenByDescending(f => f.CreatedAt)
                : query.OrderByDescending(f => f.Status).ThenByDescending(f => f.CreatedAt),
            _ => ascending
                ? query.OrderBy(f => f.CreatedAt)
                : query.OrderByDescending(f => f.CreatedAt),
        };
    }

    private static object? ParseDetail(FraudFlag flag)
    {
        if (string.IsNullOrWhiteSpace(flag.Details)) return null;

        try
        {
            return flag.Type switch
            {
                FraudFlagType.PRICE_DEVIATION =>
                    JsonSerializer.Deserialize<PriceDeviationFlagDetail>(flag.Details, DetailJsonOptions),
                FraudFlagType.HIGH_VOLUME =>
                    JsonSerializer.Deserialize<HighVolumeFlagDetail>(flag.Details, DetailJsonOptions),
                FraudFlagType.ABNORMAL_BEHAVIOR =>
                    JsonSerializer.Deserialize<AbnormalBehaviorFlagDetail>(flag.Details, DetailJsonOptions),
                FraudFlagType.MULTI_ACCOUNT =>
                    JsonSerializer.Deserialize<MultiAccountFlagDetail>(flag.Details, DetailJsonOptions),
                _ => null,
            };
        }
        catch (JsonException)
        {
            // Surface the raw payload as a fallback so the admin UI can still
            // show "something" instead of erroring out the whole detail load
            // on a malformed legacy row. Keys lower-cased so frontend
            // contracts stay predictable.
            return new { raw = flag.Details };
        }
    }

    /// <summary>
    /// Internal projection — kept private so the EF-mapped fields stay
    /// out of the public DTO surface (07 §9.2 only ships the cherry-picked
    /// columns the admin needs).
    /// </summary>
    private sealed class TransactionListProjection
    {
        public Guid Id { get; init; }
        public TransactionStatus Status { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public string? ItemIconUrl { get; init; }
        public decimal Price { get; init; }
        public StablecoinType Stablecoin { get; init; }
        public int PaymentTimeoutMinutes { get; init; }
        public DateTime CreatedAt { get; init; }
        public decimal? MarketPrice { get; init; }
    }

    private sealed class UserPartyProjection
    {
        public Guid Id { get; init; }
        public string SteamId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string? AvatarUrl { get; init; }
        public DateTime CreatedAt { get; init; }
        public int CompletedTransactionCount { get; init; }
        public decimal? SuccessfulTransactionRate { get; init; }
    }
}
