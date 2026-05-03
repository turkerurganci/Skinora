using Microsoft.EntityFrameworkCore;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Timeouts;

/// <summary>
/// Default <see cref="ITimeoutFreezeService"/> — freezes and resumes
/// transaction timeouts in line with 02 §3.3, 05 §4.4–§4.5, 09 §13.3 and the
/// 06 §3.5 state → active-deadline matrix.
/// </summary>
public sealed class TimeoutFreezeService : ITimeoutFreezeService
{
    private readonly AppDbContext _db;
    private readonly IBackgroundJobScheduler _scheduler;
    private readonly ITimeoutSchedulingService _scheduling;
    private readonly TimeProvider _clock;

    public TimeoutFreezeService(
        AppDbContext db,
        IBackgroundJobScheduler scheduler,
        ITimeoutSchedulingService scheduling,
        TimeProvider clock)
    {
        _db = db;
        _scheduler = scheduler;
        _scheduling = scheduling;
        _clock = clock;
    }

    public Task FreezeAsync(Transaction transaction, TimeoutFreezeReason reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var now = _clock.GetUtcNow().UtcDateTime;

        if (transaction.TimeoutFrozenAt is null)
        {
            transaction.TimeoutFrozenAt = now;
            transaction.TimeoutFreezeReason = reason;

            // CK_Transactions_FreezeActive (06 §3.5) requires
            // TimeoutRemainingSeconds NOT NULL whenever TimeoutFrozenAt IS NOT
            // NULL. Resolve the active phase deadline per the 06 §3.5 matrix
            // and clamp negatives to zero; ITEM_DELIVERED + FLAGGED have no
            // active deadline and so freeze with a zero remainder.
            var activeDeadline = GetActiveDeadline(transaction);
            transaction.TimeoutRemainingSeconds = ComputeRemainingSeconds(activeDeadline, now);
        }
        // else: already frozen (e.g. double-fired admin freeze, or
        // EMERGENCY_HOLD then platform-maintenance overlap). Preserve the
        // original stamp/reason/remainder; just make sure the Hangfire jobs
        // remain cancelled below.

        // Inline cancel — deliberately bypasses ITimeoutSchedulingService
        // .CancelTimeoutJobsAsync so the bulk freeze path does not pay one
        // extra DB roundtrip per transaction.
        if (!string.IsNullOrEmpty(transaction.PaymentTimeoutJobId))
        {
            _scheduler.Delete(transaction.PaymentTimeoutJobId);
            transaction.PaymentTimeoutJobId = null;
        }
        if (!string.IsNullOrEmpty(transaction.TimeoutWarningJobId))
        {
            _scheduler.Delete(transaction.TimeoutWarningJobId);
            transaction.TimeoutWarningJobId = null;
        }

        return Task.CompletedTask;
    }

    public async Task ResumeAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.TimeoutFrozenAt is null) return;

        var now = _clock.GetUtcNow().UtcDateTime;
        var remainingSeconds = transaction.TimeoutRemainingSeconds ?? 0;
        var remaining = TimeSpan.FromSeconds(remainingSeconds);
        var newActiveDeadline = now + remaining;

        // 05 §4.4 reschedule authority: TimeoutRemainingSeconds is the source
        // of truth — the active phase deadline is rewritten as
        // now + remainder, NOT as oldDeadline + elapsed (06 §8.1 "Otorite").
        SetActiveDeadline(transaction, newActiveDeadline);

        if (transaction.Status == TransactionStatus.ITEM_ESCROWED)
        {
            // ReschedulePaymentTimeoutAsync also rewrites PaymentDeadline +
            // job ids. The SetActiveDeadline call above is a no-op overlap
            // for the payment phase but kept for symmetry with the other
            // phases.
            await _scheduling.ReschedulePaymentTimeoutAsync(
                transaction.Id, remaining, newActiveDeadline, cancellationToken);
        }

        // T50 owns the freeze tracking trio (06 CK_Transactions_FreezePassive).
        // Clear after the reschedule has succeeded so an exception leaves the
        // entity in a re-runnable state.
        transaction.TimeoutFrozenAt = null;
        transaction.TimeoutFreezeReason = null;
        transaction.TimeoutRemainingSeconds = null;
    }

    public async Task<int> FreezeManyAsync(TimeoutFreezeReason reason, CancellationToken cancellationToken)
    {
        var statuses = TimeoutFreezeReasonScopes.For(reason);

        // !IsOnHold + TimeoutFrozenAt == null filters out emergency-held and
        // already-frozen rows so a maintenance window never clobbers an
        // EMERGENCY_HOLD freeze (T59 path).
        var actives = await _db.Set<Transaction>()
            .Where(t => !t.IsDeleted
                        && !t.IsOnHold
                        && t.TimeoutFrozenAt == null
                        && statuses.Contains(t.Status))
            .ToListAsync(cancellationToken);

        if (actives.Count == 0) return 0;

        foreach (var transaction in actives)
        {
            await FreezeAsync(transaction, reason, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return actives.Count;
    }

    public async Task<int> ResumeManyAsync(TimeoutFreezeReason reason, CancellationToken cancellationToken)
    {
        // Re-trigger the EMERGENCY_HOLD guard so misuse fails fast at the
        // bulk surface as well.
        _ = TimeoutFreezeReasonScopes.For(reason);

        var frozen = await _db.Set<Transaction>()
            .Where(t => !t.IsDeleted
                        && t.TimeoutFrozenAt != null
                        && t.TimeoutFreezeReason == reason)
            .ToListAsync(cancellationToken);

        if (frozen.Count == 0) return 0;

        foreach (var transaction in frozen)
        {
            await ResumeAsync(transaction, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return frozen.Count;
    }

    // 06 §3.5 state → active-deadline matrix. Mirrors the normative table so
    // freeze captures the right field's remainder and resume rewrites the
    // right field on the way back. Terminal + matrix-blank states return
    // null and freeze with a zero remainder.
    private static DateTime? GetActiveDeadline(Transaction t) => t.Status switch
    {
        TransactionStatus.CREATED => t.AcceptDeadline,
        TransactionStatus.ACCEPTED => t.TradeOfferToSellerDeadline,
        TransactionStatus.TRADE_OFFER_SENT_TO_SELLER => t.TradeOfferToSellerDeadline,
        TransactionStatus.ITEM_ESCROWED => t.PaymentDeadline,
        TransactionStatus.PAYMENT_RECEIVED => t.TradeOfferToBuyerDeadline,
        TransactionStatus.TRADE_OFFER_SENT_TO_BUYER => t.TradeOfferToBuyerDeadline,
        _ => null,
    };

    private static void SetActiveDeadline(Transaction t, DateTime newDeadline)
    {
        switch (t.Status)
        {
            case TransactionStatus.CREATED:
                t.AcceptDeadline = newDeadline;
                break;
            case TransactionStatus.ACCEPTED:
            case TransactionStatus.TRADE_OFFER_SENT_TO_SELLER:
                t.TradeOfferToSellerDeadline = newDeadline;
                break;
            case TransactionStatus.ITEM_ESCROWED:
                t.PaymentDeadline = newDeadline;
                break;
            case TransactionStatus.PAYMENT_RECEIVED:
            case TransactionStatus.TRADE_OFFER_SENT_TO_BUYER:
                t.TradeOfferToBuyerDeadline = newDeadline;
                break;
        }
    }

    private static int ComputeRemainingSeconds(DateTime? deadline, DateTime now)
    {
        if (deadline is null) return 0;
        var diff = (deadline.Value - now).TotalSeconds;
        return diff > 0 ? (int)Math.Floor(diff) : 0;
    }
}
