using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Application.Reputation;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Reputation;

/// <summary>
/// Counts a user's responsible cancellations inside the rolling window and
/// stamps <c>User.CooldownExpiresAt</c> when the limit is exceeded
/// (02 §14.2). "Responsible" mirrors the reputation aggregator definition:
/// CANCELLED_SELLER for seller, CANCELLED_BUYER for buyer, and
/// CANCELLED_TIMEOUT only for the party at fault per 06 §3.1.
/// </summary>
public sealed class CancelCooldownEvaluator : IUserCancelCooldownEvaluator
{
    private readonly AppDbContext _db;
    private readonly ICancelCooldownThresholdsProvider _thresholds;
    private readonly TimeProvider _clock;

    public CancelCooldownEvaluator(
        AppDbContext db,
        ICancelCooldownThresholdsProvider thresholds,
        TimeProvider clock)
    {
        _db = db;
        _thresholds = thresholds;
        _clock = clock;
    }

    public async Task<CooldownEvaluationResult> EvaluateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var thresholds = await _thresholds.GetAsync(cancellationToken);

        // Any unconfigured threshold disables the rule — callers can rely on a
        // partial-bootstrap environment not nuking everyone into cooldown.
        if (thresholds.LimitCount <= 0 || thresholds.WindowHours <= 0 || thresholds.CooldownHours <= 0)
            return new CooldownEvaluationResult(0, thresholds.LimitCount, thresholds.WindowHours, null);

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var windowStart = nowUtc - TimeSpan.FromHours(thresholds.WindowHours);

        var cancellations = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => (t.SellerId == userId || t.BuyerId == userId)
                        && t.CancelledAt != null
                        && t.CancelledAt >= windowStart
                        && (t.Status == TransactionStatus.CANCELLED_SELLER
                            || t.Status == TransactionStatus.CANCELLED_BUYER
                            || t.Status == TransactionStatus.CANCELLED_TIMEOUT))
            .Select(t => new { t.Id, t.Status, t.SellerId, t.BuyerId, t.CancelledAt })
            .ToListAsync(cancellationToken);

        var timeoutIds = cancellations
            .Where(c => c.Status == TransactionStatus.CANCELLED_TIMEOUT)
            .Select(c => c.Id)
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

        var responsibleCount = cancellations.Count(c => IsResponsibleFor(c.Status, c.SellerId, c.BuyerId, c.Id, userId, previousStatusByTx));

        if (responsibleCount <= thresholds.LimitCount)
            return new CooldownEvaluationResult(responsibleCount, thresholds.LimitCount, thresholds.WindowHours, null);

        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} not found.");

        var newExpiry = nowUtc + TimeSpan.FromHours(thresholds.CooldownHours);
        user.CooldownExpiresAt = newExpiry;

        return new CooldownEvaluationResult(responsibleCount, thresholds.LimitCount, thresholds.WindowHours, newExpiry);
    }

    private static bool IsResponsibleFor(
        TransactionStatus status,
        Guid sellerId,
        Guid? buyerId,
        Guid txId,
        Guid userId,
        IReadOnlyDictionary<Guid, TransactionStatus> previousStatusByTx)
    {
        var isSeller = sellerId == userId;
        var isBuyer = buyerId == userId;

        return status switch
        {
            TransactionStatus.CANCELLED_SELLER => isSeller,
            TransactionStatus.CANCELLED_BUYER => isBuyer,
            TransactionStatus.CANCELLED_TIMEOUT when previousStatusByTx.TryGetValue(txId, out var prev) =>
                prev switch
                {
                    TransactionStatus.ACCEPTED or TransactionStatus.TRADE_OFFER_SENT_TO_SELLER => isSeller,
                    TransactionStatus.CREATED or TransactionStatus.ITEM_ESCROWED or TransactionStatus.TRADE_OFFER_SENT_TO_BUYER => isBuyer,
                    _ => false
                },
            _ => false
        };
    }
}
