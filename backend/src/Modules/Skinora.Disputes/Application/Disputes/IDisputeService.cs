namespace Skinora.Disputes.Application.Disputes;

/// <summary>
/// T58 — buyer-initiated dispute pipeline (02 §10, 03 §6, 07 §7.8–§7.10).
/// Orchestrates the three endpoints (open / submit-txhash / escalate),
/// runs the type-specific auto-checker, persists the
/// <see cref="Skinora.Disputes.Domain.Entities.Dispute"/> row, and toggles
/// <see cref="Skinora.Transactions.Domain.Entities.Transaction.HasActiveDispute"/>
/// + emits the <c>DisputeAutoResolvedEvent</c> / <c>DisputeEscalatedEvent</c>
/// outbox events as part of the same unit of work.
/// </summary>
public interface IDisputeService
{
    /// <summary>
    /// <c>POST /transactions/:id/disputes</c> (07 §7.8). Buyer-only;
    /// validates per-type allowed states, runs the auto-checker, persists
    /// the dispute row.
    /// </summary>
    Task<OpenDisputeOutcome> OpenAsync(
        Guid callerUserId,
        Guid transactionId,
        OpenDisputeRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /transactions/:id/disputes/:disputeId/submit-txhash</c>
    /// (07 §7.9). Re-runs the payment auto-checker against the buyer-supplied
    /// hash; closes the dispute on success.
    /// </summary>
    Task<SubmitTxHashOutcome> SubmitTxHashAsync(
        Guid callerUserId,
        Guid transactionId,
        Guid disputeId,
        SubmitTxHashRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /transactions/:id/disputes/:disputeId/escalate</c>
    /// (07 §7.10). Promotes an OPEN dispute to ESCALATED, persists the
    /// buyer-supplied detail, and notifies the buyer.
    /// </summary>
    Task<EscalateDisputeOutcome> EscalateAsync(
        Guid callerUserId,
        Guid transactionId,
        Guid disputeId,
        EscalateDisputeRequest request,
        CancellationToken cancellationToken);
}
