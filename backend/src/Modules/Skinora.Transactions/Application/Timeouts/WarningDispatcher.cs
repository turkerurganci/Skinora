using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Timeouts;

/// <summary>
/// Default <see cref="IWarningDispatcher"/> — Hangfire job target for the
/// per-transaction payment timeout warning (T48 — 02 §3.4, 05 §4.4).
/// Implements the 09 §13.3 state-validation no-op pattern, atomically stamps
/// <see cref="Transaction.TimeoutWarningSentAt"/> and publishes a
/// <see cref="TimeoutWarningEvent"/> to the outbox so the Notifications module
/// fans out to all enabled channels (in-app + email + Telegram + Discord).
/// </summary>
public sealed class WarningDispatcher : IWarningDispatcher
{
    private readonly AppDbContext _db;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<WarningDispatcher> _logger;

    public WarningDispatcher(
        AppDbContext db,
        IOutboxService outbox,
        TimeProvider clock,
        ILogger<WarningDispatcher> logger)
    {
        _db = db;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task DispatchWarningAsync(Guid transactionId)
    {
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted);
        if (transaction is null) return;

        // 09 §13.3 — defensive guards. State, freeze, hold, double-warn must
        // all hold for the dispatch to fire. Any miss is a no-op.
        if (transaction.IsOnHold) return;
        if (transaction.TimeoutFrozenAt is not null) return;
        if (transaction.TimeoutWarningSentAt is not null) return;

        // WP7 (P2P-DeliveryTimeoutWarning) — the warning now covers the
        // delivery phase too. 02 §3.4 and 03 §4.5 define it for ALL timeouts,
        // and the delivery phase was the one that had none: in P2P a seller who
        // does not deliver is recorded as the party at fault (06 §3.1), so
        // letting that clock run out unannounced penalises someone who was
        // never told. Which party is warned follows who owes the action —
        // the buyer owes the payment, the seller owes the item.
        Guid recipientId;
        DateTime deadline;
        switch (transaction.Status)
        {
            case TransactionStatus.SELLER_CONFIRMED:
                if (transaction.PaymentDeadline is null || transaction.BuyerId is null) return;
                recipientId = transaction.BuyerId.Value;
                deadline = transaction.PaymentDeadline.Value;
                break;

            case TransactionStatus.PAYMENT_RECEIVED:
                if (transaction.DeliveryDeadline is null) return;
                recipientId = transaction.SellerId;
                deadline = transaction.DeliveryDeadline.Value;
                break;

            default:
                return;
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var remaining = deadline - nowUtc;
        if (remaining <= TimeSpan.Zero)
        {
            // Deadline already passed — the timeout executor will drive the
            // CANCELLED_TIMEOUT transition. Sending a "süre dolmak üzere"
            // notification at this point would be misleading, so we no-op.
            return;
        }

        var remainingMinutes = (int)Math.Floor(remaining.TotalMinutes);

        transaction.TimeoutWarningSentAt = nowUtc;

        await _outbox.PublishAsync(
            new TimeoutWarningEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                RecipientUserId: recipientId,
                ItemName: transaction.ItemName,
                RemainingMinutes: remainingMinutes,
                OccurredAt: nowUtc));

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Timeout warning published for transaction {TransactionId} (RemainingMinutes={RemainingMinutes}).",
            transaction.Id, remainingMinutes);
    }
}
