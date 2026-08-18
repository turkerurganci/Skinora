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
///   <item><c>PreviousStatus = ACCEPTED</c> (hazırlık onayı, adım 3) → SELLER</item>
///   <item><c>PreviousStatus = SELLER_CONFIRMED</c> (ödeme timeout, adım 4) → BUYER</item>
///   <item><c>PreviousStatus = PAYMENT_RECEIVED</c> (teslimat, adım 6) → SELLER</item>
/// </list>
/// <para>
/// <see cref="TransactionStatus.CANCELLED_ADMIN"/> rows are excluded from
/// both numerator and denominator (02 §13 — platform decision, not user
/// fault).
/// </para>
/// <para>
/// <see cref="TransactionStatus.REFUNDED"/> is split rather than excluded
/// wholesale (T129 — 06 §3.1). Its two producers say opposite things about
/// fault: an admin dispute refund is a platform ruling and stays out for the
/// same reason CANCELLED_ADMIN does, while a settlement reversal
/// (<c>DeliveryReversedAt</c> set) is the platform having WATCHED the seller
/// take the item back after being paid. Only the second counts, and only
/// against the seller.
/// </para>
/// <para>
/// <see cref="TransactionStatus.CANCELLED_TIMEOUT"/> is split the same way
/// (T131 — validation finding B1). A delivery timeout that ran because an
/// ADMIN ruled on a misdelivery dispute (<c>TimeoutReleasedByAdminRulingAt</c>
/// set) is excluded outright, on the reasoning that excludes CANCELLED_ADMIN:
/// the row records a platform decision, and the decision it records is the
/// admin CLEARING this seller (03 §6.4). Counting it would take the one case
/// where a human explicitly found no fault and turn it into the heaviest
/// negative signal a seller can carry — with no correction surface, since
/// reputation is recomputed from the rows themselves.
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
                            // T131 — not the admin-released kind of
                            // CANCELLED_TIMEOUT (finding B1). Same shape as the
                            // REFUNDED filter below and the same reason: the
                            // status has two producers, and this one is a
                            // platform decision that CLEARED the seller.
                            || (t.Status == TransactionStatus.CANCELLED_TIMEOUT
                                && t.TimeoutReleasedByAdminRulingAt == null)
                            // T129 — only the reversal kind of REFUNDED. The
                            // filter is what keeps 02 §13 intact: an admin
                            // dispute refund is a platform decision and stays
                            // out of the formula entirely, exactly like
                            // CANCELLED_ADMIN.
                            || (t.Status == TransactionStatus.REFUNDED
                                && t.DeliveryReversedAt != null)))
            .Select(t => new TxRow(
                t.Id,
                t.Status,
                t.SellerId,
                t.BuyerId,
                t.CreatedAt,
                t.CancelledAt,
                t.CompletedAt,
                t.DeliveryReversedAt,
                t.TimeoutReleasedByAdminRulingAt))
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
        DateTime? CompletedAt,
        DateTime? DeliveryReversedAt,
        DateTime? TimeoutReleasedByAdminRulingAt);

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

            // T129 — trade reversed at settlement (02 §4.5.1). Charged to the
            // SELLER alone, and the asymmetry is the finding itself: the item
            // came back to them after the buyer had paid, which is proven seller
            // fault rather than a platform ruling. Without this arm the heaviest
            // fraud in the model would leave a reputation score untouched, since
            // the only other trace is a fraud flag an admin has to go read.
            //
            // The row reaches here only when DeliveryReversedAt is set (the
            // query filters on it), so no second test is needed — but the guard
            // is written out anyway because REFUNDED has a second, excluded
            // producer (admin dispute) and a future widening of the query must
            // not silently start charging sellers for admin decisions.
            case TransactionStatus.REFUNDED:
                if (row.DeliveryReversedAt is null) return new(false, false);
                return isSeller ? new(true, false) : new(false, false);

            case TransactionStatus.CANCELLED_TIMEOUT:
                // T131 (finding B1) — the query already filters these out, and
                // the guard is written out anyway for the reason the REFUNDED
                // one above is: CANCELLED_TIMEOUT has two producers now, and a
                // future widening of the query must not silently start charging
                // a seller for the cancellation an admin's ruling authorised.
                if (row.TimeoutReleasedByAdminRulingAt is not null) return new(false, false);

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
        TransactionStatus.SELLER_CONFIRMED => TimeoutResponsibility.Buyer,

        // v3.0 — the delivery phase flipped from BUYER to SELLER. In the
        // custodial model this window waited on the buyer accepting a
        // platform-sent offer; in P2P the seller is the one who must send the
        // trade, so the delay is theirs (02 §3.1). This makes non-delivery the
        // dominant negative signal in a seller's reputation.
        TransactionStatus.PAYMENT_RECEIVED => TimeoutResponsibility.Seller,

        _ => TimeoutResponsibility.None
    };

    private enum TimeoutResponsibility { None, Seller, Buyer }
}
