using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.PostCancel;
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
    private readonly ILogger<TimeoutExecutor> _logger;

    public TimeoutExecutor(
        AppDbContext db,
        TimeProvider clock,
        ITimeoutSideEffectPublisher sideEffects,
        IPostCancelMonitorStarter postCancelMonitor,
        ILogger<TimeoutExecutor> logger)
    {
        _db = db;
        _clock = clock;
        _sideEffects = sideEffects;
        _postCancelMonitor = postCancelMonitor;
        _logger = logger;
    }

    public async Task ExecutePaymentTimeoutAsync(Guid transactionId)
    {
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted);
        if (transaction is null) return;

        // 09 §13.3 — defensive guards. State, freeze, hold and deadline must
        // all hold for the trigger to fire. Any miss is a no-op.
        if (transaction.Status != TransactionStatus.ITEM_ESCROWED) return;
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
        // happens from ITEM_ESCROWED, so PaymentAddress is guaranteed to be
        // allocated (T44). Use CancelledAt as the anchor when set; otherwise
        // fall back to wall-clock now (defensive — state machine stamps it).
        var cancelledAt = transaction.CancelledAt ?? _clock.GetUtcNow().UtcDateTime;
        await _postCancelMonitor.RequestStartAsync(transaction.Id, cancelledAt, CancellationToken.None);

        await _db.SaveChangesAsync();
    }
}
