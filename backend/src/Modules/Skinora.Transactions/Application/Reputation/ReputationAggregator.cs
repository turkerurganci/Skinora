using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Application.Reputation;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Reputation;

/// <summary>
/// EF Core-backed <see cref="IReputationAggregator"/> implementation.
/// Reads <see cref="Transaction"/> + <see cref="TransactionHistory"/>, applies
/// the 06 §3.1 responsibility map and the 02 §14.1 wash-trading filter, then
/// writes the recomputed <c>CompletedTransactionCount</c> and
/// <c>SuccessfulTransactionRate</c> back onto the tracked <see cref="User"/>.
/// </summary>
/// <remarks>
/// <para>
/// Responsibility map for <see cref="TransactionStatus.CANCELLED_TIMEOUT"/>
/// follows 06 §3.1 + 03 §4.1–§4.4:
/// </para>
/// <list type="bullet">
///   <item><c>PreviousStatus = CREATED</c> (alıcı kabul timeout, adım 2) → BUYER (skipped when BuyerId is null — no party to attribute)</item>
///   <item><c>PreviousStatus = ACCEPTED | TRADE_OFFER_SENT_TO_SELLER</c> (adım 3) → SELLER</item>
///   <item><c>PreviousStatus = ITEM_ESCROWED</c> (ödeme timeout, adım 4) → BUYER</item>
///   <item><c>PreviousStatus = TRADE_OFFER_SENT_TO_BUYER</c> (adım 6) → BUYER</item>
/// </list>
/// <para>
/// <see cref="TransactionStatus.CANCELLED_ADMIN"/> rows are excluded from
/// both numerator and denominator (02 §13 — platform decision, not user
/// fault).
/// </para>
/// </remarks>
public sealed class ReputationAggregator : IReputationAggregator
{
    private readonly AppDbContext _db;

    public ReputationAggregator(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ReputationSnapshot> RecomputeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} not found.");

        // Pull every transaction the user is a party to that the formula cares
        // about. CANCELLED_ADMIN rows are excluded at the DB layer per 02 §13.
        var rows = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => (t.SellerId == userId || t.BuyerId == userId)
                        && (t.Status == TransactionStatus.COMPLETED
                            || t.Status == TransactionStatus.CANCELLED_SELLER
                            || t.Status == TransactionStatus.CANCELLED_BUYER
                            || t.Status == TransactionStatus.CANCELLED_TIMEOUT))
            .Select(t => new TxRow(
                t.Id,
                t.Status,
                t.SellerId,
                t.BuyerId,
                t.CreatedAt,
                t.CancelledAt,
                t.CompletedAt))
            .ToListAsync(cancellationToken);

        // Raw COMPLETED count — wash filter intentionally NOT applied
        // (02 §13 / 06 §3.1 scope it to the rate denominator).
        var completedCount = rows.Count(r => r.Status == TransactionStatus.COMPLETED);

        // Need PreviousStatus only for CANCELLED_TIMEOUT rows that involve
        // this user; pull from TransactionHistory.
        var timeoutIds = rows
            .Where(r => r.Status == TransactionStatus.CANCELLED_TIMEOUT)
            .Select(r => r.Id)
            .ToList();

        var previousStatusByTx = timeoutIds.Count == 0
            ? new Dictionary<Guid, TransactionStatus>()
            : await _db.Set<TransactionHistory>()
                .AsNoTracking()
                .Where(h => timeoutIds.Contains(h.TransactionId)
                            && h.NewStatus == TransactionStatus.CANCELLED_TIMEOUT
                            && h.PreviousStatus != null)
                .GroupBy(h => h.TransactionId)
                .Select(g => new
                {
                    TxId = g.Key,
                    PreviousStatus = g.OrderByDescending(h => h.CreatedAt).First().PreviousStatus!.Value
                })
                .ToDictionaryAsync(x => x.TxId, x => x.PreviousStatus, cancellationToken);

        // Map each row to (affects-this-user?, counts-as-success?).
        var classified = rows
            .Select(r => new ClassifiedRow(
                r,
                ResponsibilityFor(r, userId, previousStatusByTx)))
            .Where(c => c.Effect.AffectsUser)
            .ToList();

        // Wash trading filter on the unordered (sellerId, buyerId) pair.
        var filtered = WashTradingFilter.Apply(
            classified,
            c => (c.Tx.SellerId, c.Tx.BuyerId ?? Guid.Empty),
            c => c.Tx.CompletedAt ?? c.Tx.CancelledAt ?? c.Tx.CreatedAt);

        var counted = filtered.Where(v => v.Counted).Select(v => v.Row).ToList();
        var denominator = counted.Count;
        var numerator = counted.Count(c => c.Effect.IsSuccess);

        decimal? rate = denominator == 0
            ? null
            : Math.Round((decimal)numerator / denominator, 4, MidpointRounding.ToZero);

        user.CompletedTransactionCount = completedCount;
        user.SuccessfulTransactionRate = rate;

        return new ReputationSnapshot(completedCount, rate);
    }

    private record struct TxRow(
        Guid Id,
        TransactionStatus Status,
        Guid SellerId,
        Guid? BuyerId,
        DateTime CreatedAt,
        DateTime? CancelledAt,
        DateTime? CompletedAt);

    private readonly record struct ClassifiedRow(TxRow Tx, ResponsibilityEffect Effect);

    private readonly record struct ResponsibilityEffect(bool AffectsUser, bool IsSuccess);

    private static ResponsibilityEffect ResponsibilityFor(
        TxRow row,
        Guid userId,
        IReadOnlyDictionary<Guid, TransactionStatus> previousStatusByTx)
    {
        var isSeller = row.SellerId == userId;
        var isBuyer = row.BuyerId == userId;

        switch (row.Status)
        {
            case TransactionStatus.COMPLETED:
                // Both parties get a successful tally (denominator + numerator),
                // provided they ARE a party.
                return new(isSeller || isBuyer, true);

            case TransactionStatus.CANCELLED_SELLER:
                return isSeller ? new(true, false) : new(false, false);

            case TransactionStatus.CANCELLED_BUYER:
                return isBuyer ? new(true, false) : new(false, false);

            case TransactionStatus.CANCELLED_TIMEOUT:
                if (!previousStatusByTx.TryGetValue(row.Id, out var previous))
                    return new(false, false);

                var responsible = ResponsibleForTimeout(previous);
                return responsible switch
                {
                    TimeoutResponsibility.Seller => isSeller ? new(true, false) : new(false, false),
                    TimeoutResponsibility.Buyer => isBuyer ? new(true, false) : new(false, false),
                    _ => new(false, false)
                };

            default:
                return new(false, false);
        }
    }

    private static TimeoutResponsibility ResponsibleForTimeout(TransactionStatus previousStatus) => previousStatus switch
    {
        TransactionStatus.CREATED => TimeoutResponsibility.Buyer,
        TransactionStatus.ACCEPTED => TimeoutResponsibility.Seller,
        TransactionStatus.TRADE_OFFER_SENT_TO_SELLER => TimeoutResponsibility.Seller,
        TransactionStatus.ITEM_ESCROWED => TimeoutResponsibility.Buyer,
        TransactionStatus.TRADE_OFFER_SENT_TO_BUYER => TimeoutResponsibility.Buyer,
        _ => TimeoutResponsibility.None
    };

    private enum TimeoutResponsibility { None, Seller, Buyer }
}
