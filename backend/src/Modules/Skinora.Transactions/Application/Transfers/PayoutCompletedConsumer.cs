using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Application.Reputation;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// WP1 completion leg — MediatR notification handler that consumes
/// <see cref="PayoutCompletedEvent"/> (published by
/// <see cref="OutgoingTransferConfirmationJob"/> when a SELLER_PAYOUT row
/// confirms on chain) and fires <c>TransactionTrigger.Complete</c> to advance
/// the transaction ITEM_DELIVERED → COMPLETED (03 §2.4 step 6). COMPLETED's
/// OnEntry stamps <c>CompletedAt</c>.
/// </summary>
/// <remarks>
/// Idempotency is domain-level (status guard), not <c>IProcessedEventStore</c>:
/// the handler mutates Transaction state and owns its own
/// <c>SaveChangesAsync</c>, so an outbox redelivery simply finds the
/// transaction already past ITEM_DELIVERED and no-ops. A transaction that is
/// on emergency hold cannot be completed (the state machine rejects every
/// trigger while held); it is logged and left for the hold-release path. A
/// concurrent-modification failure propagates so the outbox retries.
/// </remarks>
public sealed class PayoutCompletedConsumer
    : INotificationHandler<PayoutCompletedEvent>
{
    private readonly AppDbContext _db;
    private readonly ITransactionReputationRefresher _reputation;
    private readonly ILogger<PayoutCompletedConsumer> _logger;

    public PayoutCompletedConsumer(
        AppDbContext db,
        ITransactionReputationRefresher reputation,
        ILogger<PayoutCompletedConsumer> logger)
    {
        _db = db;
        _reputation = reputation;
        _logger = logger;
    }

    public async Task Handle(
        PayoutCompletedEvent notification, CancellationToken cancellationToken)
    {
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == notification.TransactionId, cancellationToken);
        if (transaction is null)
        {
            _logger.LogWarning(
                "PayoutCompleted: transaction {TransactionId} not found — cannot complete.",
                notification.TransactionId);
            return;
        }

        // Domain idempotency — a redelivered event (or a transaction already
        // advanced by an earlier tick) finds it past ITEM_DELIVERED. No-op.
        if (transaction.Status != TransactionStatus.ITEM_DELIVERED)
        {
            _logger.LogInformation(
                "PayoutCompleted: transaction {TransactionId} is {Status}, not ITEM_DELIVERED — already handled, skipping.",
                transaction.Id, transaction.Status);
            return;
        }

        // A held transaction cannot be completed (state machine rejects all
        // triggers while IsOnHold). Leave it for the hold-release flow rather
        // than throwing into an outbox retry loop.
        if (transaction.IsOnHold)
        {
            _logger.LogWarning(
                "PayoutCompleted: transaction {TransactionId} is on emergency hold — completion deferred to hold release.",
                transaction.Id);
            return;
        }

        // 09 §9.2 — caller-side state machine without an expected RowVersion:
        // the row was loaded fresh in this scope and EF Core's optimistic
        // concurrency token still guards the SaveChanges below.
        var machine = new TransactionStateMachine(transaction);
        if (!machine.CanFire(TransactionTrigger.Complete))
        {
            _logger.LogWarning(
                "PayoutCompleted: transaction {TransactionId} state {State} does not permit Complete — skipping.",
                transaction.Id, transaction.Status);
            return;
        }

        var previousStatus = transaction.Status;
        try
        {
            machine.Fire(TransactionTrigger.Complete);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex,
                "PayoutCompleted: Complete fire rejected for transaction {TransactionId} state {State}.",
                transaction.Id, transaction.Status);
            return;
        }

        // WP15 — audit-trail row (06 §3.6) for the SYSTEM-driven completion.
        TransactionHistoryRecorder.Record(
            _db, transaction, previousStatus, TransactionTrigger.Complete,
            ActorType.SYSTEM, SeedConstants.SystemUserId,
            transaction.CompletedAt ?? DateTime.UtcNow);

        // Flush COMPLETED + history first; the reputation aggregator reads
        // Transaction rows AsNoTracking so it must observe the committed status.
        await _db.SaveChangesAsync(cancellationToken);

        // WP15 — recompute the denormalized reputation snapshot for both parties:
        // a successful trade bumps each side's CompletedTransactionCount and
        // SuccessfulTransactionRate (06 §8.2). No cooldown — completion is not a
        // cancellation. This consumer shares the AppDbContext with the outbox
        // dispatcher, so it deliberately avoids an explicit transaction; the
        // recompute is idempotent + eventual (06 §8.2) and self-heals on retry.
        await _reputation.RefreshAsync(
            transaction.SellerId, transaction.BuyerId, evaluateCooldown: false, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PayoutCompleted: transaction {TransactionId} → COMPLETED (payout tx {TxHash}, net {Amount}).",
            transaction.Id, notification.PayoutTxHash, notification.NetAmount);
    }
}
