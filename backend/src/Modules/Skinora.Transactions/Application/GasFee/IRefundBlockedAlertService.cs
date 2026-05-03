namespace Skinora.Transactions.Application.GasFee;

/// <summary>
/// Side-effect tier for <see cref="IRefundDecisionService"/> — when a
/// decision returns <see cref="RefundOutcome.Block"/> the caller invokes
/// this service to (1) stage an <c>AuditLog</c> row with action
/// <c>REFUND_BLOCKED</c> (06 §3.20, 09 §14.4) and (2) enqueue an
/// <see cref="Skinora.Shared.Events.RefundBlockedAdminAlertEvent"/> for
/// downstream admin notification fan-out (T78–T80).
/// </summary>
/// <remarks>
/// The service stages writes on the change tracker; the caller owns the
/// surrounding transaction and <c>SaveChangesAsync</c>. This mirrors
/// <c>IAuditLogger</c>'s unit-of-work discipline so the audit row, the
/// outbox row and the business state change all commit atomically
/// (09 §13.3).
/// </remarks>
public interface IRefundBlockedAlertService
{
    Task RaiseAsync(Guid transactionId, RefundDecision decision, CancellationToken cancellationToken);
}
