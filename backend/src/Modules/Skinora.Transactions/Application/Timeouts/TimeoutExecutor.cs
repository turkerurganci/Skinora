using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Persistence;
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
    private readonly ILogger<TimeoutExecutor> _logger;

    public TimeoutExecutor(AppDbContext db, TimeProvider clock, ILogger<TimeoutExecutor> logger)
    {
        _db = db;
        _clock = clock;
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

        // Side effects (refund, notification fan-out) are forward-deferred to
        // T49 — T47 only flips the state.
        await _db.SaveChangesAsync();
    }
}

/// <summary>
/// Default <see cref="IWarningDispatcher"/> — marks the warning as sent so
/// the duplicate-warning guard (09 §13.3) is honored. The actual notification
/// fan-out (Email/Telegram/Discord/SignalR) is forward-deferred to T48.
/// </summary>
public sealed class StubWarningDispatcher : IWarningDispatcher
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<StubWarningDispatcher> _logger;

    public StubWarningDispatcher(AppDbContext db, TimeProvider clock, ILogger<StubWarningDispatcher> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task DispatchWarningAsync(Guid transactionId)
    {
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted);
        if (transaction is null) return;

        // 09 §13.3 — same no-op pattern as the executor.
        if (transaction.Status != TransactionStatus.ITEM_ESCROWED) return;
        if (transaction.IsOnHold) return;
        if (transaction.TimeoutFrozenAt is not null) return;
        if (transaction.TimeoutWarningSentAt is not null) return;

        transaction.TimeoutWarningSentAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync();

        // T48 will replace this stub with the real notification dispatch
        // (Email/Telegram/Discord/SignalR) — for now we just log so the
        // scheduler chain is observable end-to-end.
        _logger.LogInformation(
            "Timeout warning marked sent for transaction {TransactionId} (T48 fan-out pending).",
            transaction.Id);
    }
}
