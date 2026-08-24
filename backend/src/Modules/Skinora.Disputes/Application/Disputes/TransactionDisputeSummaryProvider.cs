using Microsoft.EntityFrameworkCore;
using Skinora.Disputes.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Lifecycle;

namespace Skinora.Disputes.Application.Disputes;

/// <summary>
/// WP6b (T133a-DisputeBlockNulls) — supplies the 07 §7.5 <c>dispute</c> block
/// to <c>TransactionDetailService</c>. Implements a port declared in
/// <c>Skinora.Transactions</c> so the dependency runs Disputes → Transactions,
/// the direction the project references already allow.
/// </summary>
public sealed class TransactionDisputeSummaryProvider : ITransactionDisputeSummaryProvider
{
    private readonly AppDbContext _db;

    public TransactionDisputeSummaryProvider(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DisputeSummaryDto?> GetLatestAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        // Soft-deleted rows are excluded by the global query filter.
        var dispute = await _db.Set<Dispute>()
            .AsNoTracking()
            .Where(d => d.TransactionId == transactionId)
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .Select(d => new
            {
                d.Id,
                d.Type,
                d.Status,
                d.SystemCheckResult,
                d.CreatedAt,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (dispute is null) return null;

        return new DisputeSummaryDto(
            Id: dispute.Id,
            Type: dispute.Type.ToString(),
            Status: dispute.Status.ToString(),
            AutoCheckResult: dispute.SystemCheckResult,
            // Derived from the dispute's own state rather than re-run through
            // the auto-checkers: these mirror the guards the endpoints actually
            // enforce. submit-txhash requires a PAYMENT dispute still OPEN
            // (DisputeService stages 2–3); escalate refuses ESCALATED and
            // CLOSED, and the two admin resolution outcomes are terminal too.
            //
            // Re-running an auto-check from a read path would be both expensive
            // and misleading — the checker is what a POST runs, and its answer
            // belongs to that moment, not to every page load.
            CanSubmitTxHash: dispute.Type == DisputeType.PAYMENT
                             && dispute.Status == DisputeStatus.OPEN,
            CanEscalate: dispute.Status != DisputeStatus.ESCALATED
                         && dispute.Status != DisputeStatus.CLOSED
                         && dispute.Status != DisputeStatus.RESOLVED_FOR_SELLER
                         && dispute.Status != DisputeStatus.RESOLVED_FOR_BUYER,
            CreatedAt: dispute.CreatedAt);
    }
}
