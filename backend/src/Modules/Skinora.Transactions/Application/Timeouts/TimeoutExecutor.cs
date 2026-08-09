using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Application.PostCancel;
using Skinora.Transactions.Application.Reputation;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Transactions.Application.Timeouts;

/// <summary>
/// Default <see cref="ITimeoutExecutor"/> — Hangfire job target for the
/// per-transaction payment timeout. Implements the 09 §13.3 state-validation
/// no-op pattern, so an orphan or stale job (atomicity gap, retry, freeze in
/// flight) cannot push a transaction off its track.
/// </summary>
public sealed class TimeoutExecutor : ITimeoutExecutor
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ITimeoutSideEffectPublisher _sideEffects;
    private readonly IPostCancelMonitorStarter _postCancelMonitor;
    private readonly ITransactionReputationRefresher _reputation;
    private readonly ILogger<TimeoutExecutor> _logger;

    public TimeoutExecutor(
        AppDbContext db,
        TimeProvider clock,
        ITimeoutSideEffectPublisher sideEffects,
        IPostCancelMonitorStarter postCancelMonitor,
        ITransactionReputationRefresher reputation,
        ILogger<TimeoutExecutor> logger)
    {
        _db = db;
        _clock = clock;
        _sideEffects = sideEffects;
        _postCancelMonitor = postCancelMonitor;
        _reputation = reputation;
        _logger = logger;
    }

    public async Task ExecutePaymentTimeoutAsync(Guid transactionId)
    {
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted);
        if (transaction is null) return;

        // 09 §13.3 — defensive guards. State, freeze, hold and deadline must
        // all hold for the trigger to fire. Any miss is a no-op.
        if (transaction.Status != TransactionStatus.SELLER_CONFIRMED) return;
        if (transaction.IsOnHold) return;
        if (transaction.TimeoutFrozenAt is not null) return;
        if (transaction.PaymentDeadline > _clock.GetUtcNow().UtcDateTime) return;

        var previousStatus = transaction.Status;
        var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
        try
        {
            machine.Fire(TransactionTrigger.Timeout);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(
                ex,
                "Payment timeout trigger refused for transaction {TransactionId} ({ErrorCode}).",
                transaction.Id, ex.ErrorCode);
            return;
        }

        await _sideEffects.PublishAsync(transaction, previousStatus);

        // T75 — post-cancel monitoring start request. The timeout path always
        // happens from SELLER_CONFIRMED, so PaymentAddress is guaranteed to be
        // allocated (T44). Use CancelledAt as the anchor when set; otherwise
        // fall back to wall-clock now (defensive — state machine stamps it).
        var cancelledAt = transaction.CancelledAt ?? _clock.GetUtcNow().UtcDateTime;
        await _postCancelMonitor.RequestStartAsync(transaction.Id, cancelledAt, CancellationToken.None);

        // WP15 — audit-trail row (06 §3.6). PreviousStatus is the ONLY source the
        // reputation/cooldown responsibility map reads to attribute the timeout to
        // the at-fault party (06 §3.1) — without this row, timeouts are silently
        // dropped from reputation.
        TransactionHistoryRecorder.Record(
            _db, transaction, previousStatus, TransactionTrigger.Timeout,
            ActorType.SYSTEM, SeedConstants.SystemUserId, cancelledAt);

        // WP15 — flush the timeout transition + history, then recompute
        // reputation/cooldown. The aggregator/cooldown read AsNoTracking and
        // resolve the responsible party from the just-written history
        // PreviousStatus, so the flip must be committed-in-transaction first.
        // Both writes share one DB transaction (09 §13.3).
        await using var dbTx = await _db.Database.BeginTransactionAsync();
        await _db.SaveChangesAsync();
        await _reputation.RefreshAsync(
            transaction.SellerId, transaction.BuyerId, evaluateCooldown: true, CancellationToken.None);
        await _db.SaveChangesAsync();
        await dbTx.CommitAsync();
    }
}
