using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Reputation;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Tests.Helpers;

/// <summary>
/// Records every <see cref="INonDeliveryAbuseEvaluator"/> request together with
/// the status the DATABASE held at call time. The real evaluator reads with
/// <c>AsNoTracking</c>, so a call placed before the terminal transition is
/// flushed would see the pre-cancel status and never count the event — the
/// recorded status is the only way a hook-site test can prove the call sits
/// after the flush.
/// </summary>
public sealed class RecordingNonDeliveryAbuseEvaluator : INonDeliveryAbuseEvaluator
{
    private readonly AppDbContext _db;

    public RecordingNonDeliveryAbuseEvaluator(AppDbContext db) => _db = db;

    public List<(Guid TransactionId, TransactionStatus StatusInDb)> Calls { get; } = [];

    public async Task<NonDeliveryAbuseOutcome> EvaluateAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var status = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.Id == transactionId)
            .Select(t => t.Status)
            .SingleAsync(cancellationToken);
        Calls.Add((transactionId, status));
        return new NonDeliveryAbuseOutcome(NonDeliveryAbuseAction.NotANonDeliveryEvent, 0);
    }
}
