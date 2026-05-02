using Skinora.Shared.Enums;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Timeouts;

/// <summary>
/// Publishes the outbox events that follow a successful <c>Timeout</c> trigger
/// (T49 — 02 §3.2, 03 §4.1–§4.4). Centralises the phase-to-side-effect mapping
/// so <see cref="ITimeoutExecutor"/> and <see cref="IDeadlineScannerJob"/> stay
/// thin and produce identical fan-outs for the same phase.
/// </summary>
/// <remarks>
/// The publisher only enlists outbox rows on the change tracker — it does not
/// call <c>SaveChangesAsync</c>. The caller commits the state flip and the
/// outbox enqueue in a single transaction (09 §13.3 atomicity boundary).
/// </remarks>
public interface ITimeoutSideEffectPublisher
{
    /// <summary>
    /// Publishes the per-phase notification + refund events for a transaction
    /// that just transitioned to <c>CANCELLED_TIMEOUT</c>.
    /// </summary>
    /// <param name="transaction">Transaction snapshot in its post-flip state (Status == CANCELLED_TIMEOUT).</param>
    /// <param name="previousStatus">Status the transaction held before the trigger fired — drives phase-specific side effects.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync(
        Transaction transaction,
        TransactionStatus previousStatus,
        CancellationToken cancellationToken = default);
}
