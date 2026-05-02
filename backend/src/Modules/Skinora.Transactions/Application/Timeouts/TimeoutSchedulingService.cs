using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Timeouts;

/// <summary>
/// Default <see cref="ITimeoutSchedulingService"/> — schedules and cancels
/// per-transaction Hangfire jobs (05 §4.4 + 09 §13.3).
/// </summary>
public sealed class TimeoutSchedulingService : ITimeoutSchedulingService
{
    /// <summary>Settings key for the warning ratio (07 §9.8 / 02 §3.4).</summary>
    public const string WarningRatioKey = "timeout_warning_ratio";

    private readonly AppDbContext _db;
    private readonly IBackgroundJobScheduler _scheduler;
    private readonly TimeProvider _clock;

    public TimeoutSchedulingService(
        AppDbContext db, IBackgroundJobScheduler scheduler, TimeProvider clock)
    {
        _db = db;
        _scheduler = scheduler;
        _clock = clock;
    }

    public async Task<TimeoutJobIds> SchedulePaymentTimeoutAsync(
        Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await LoadAsync(transactionId, cancellationToken);
        if (transaction.Status != TransactionStatus.ITEM_ESCROWED)
            throw new InvalidOperationException(
                $"SchedulePaymentTimeout requires ITEM_ESCROWED, got {transaction.Status}.");
        if (transaction.PaymentDeadline is null)
            throw new InvalidOperationException(
                "SchedulePaymentTimeout requires PaymentDeadline to be set (06 §3.5).");

        var now = _clock.GetUtcNow().UtcDateTime;
        var paymentDelay = transaction.PaymentDeadline.Value - now;
        if (paymentDelay < TimeSpan.Zero) paymentDelay = TimeSpan.Zero;

        var paymentJobId = _scheduler.Schedule<ITimeoutExecutor>(
            x => x.ExecutePaymentTimeoutAsync(transaction.Id),
            paymentDelay);
        transaction.PaymentTimeoutJobId = paymentJobId;

        var warningRatio = await ReadWarningRatioAsync(cancellationToken);
        string? warningJobId = null;
        if (warningRatio is { } ratio && ratio > 0m && ratio < 1m)
        {
            // Warning fires "ratio × paymentTimeoutMinutes" from start (02 §3.4):
            // we anchor it to the same payment-deadline arithmetic so resume
            // after freeze keeps the relative offset.
            var warningDelay = TimeSpan.FromTicks((long)(paymentDelay.Ticks * (double)ratio));
            if (warningDelay < TimeSpan.Zero) warningDelay = TimeSpan.Zero;
            warningJobId = _scheduler.Schedule<IWarningDispatcher>(
                x => x.DispatchWarningAsync(transaction.Id),
                warningDelay);
            transaction.TimeoutWarningJobId = warningJobId;
        }

        return new TimeoutJobIds(paymentJobId, warningJobId);
    }

    public async Task CancelTimeoutJobsAsync(
        Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await LoadAsync(transactionId, cancellationToken);
        if (!string.IsNullOrEmpty(transaction.PaymentTimeoutJobId))
        {
            _scheduler.Delete(transaction.PaymentTimeoutJobId);
            transaction.PaymentTimeoutJobId = null;
        }
        if (!string.IsNullOrEmpty(transaction.TimeoutWarningJobId))
        {
            _scheduler.Delete(transaction.TimeoutWarningJobId);
            transaction.TimeoutWarningJobId = null;
            transaction.TimeoutWarningSentAt = null;
        }
    }

    public async Task<TimeoutJobIds> ReschedulePaymentTimeoutAsync(
        Guid transactionId,
        TimeSpan remaining,
        DateTime newPaymentDeadlineUtc,
        CancellationToken cancellationToken)
    {
        var transaction = await LoadAsync(transactionId, cancellationToken);
        if (transaction.Status != TransactionStatus.ITEM_ESCROWED)
            throw new InvalidOperationException(
                $"ReschedulePaymentTimeout requires ITEM_ESCROWED, got {transaction.Status}.");

        if (!string.IsNullOrEmpty(transaction.PaymentTimeoutJobId))
            _scheduler.Delete(transaction.PaymentTimeoutJobId);
        if (!string.IsNullOrEmpty(transaction.TimeoutWarningJobId))
            _scheduler.Delete(transaction.TimeoutWarningJobId);

        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        transaction.PaymentDeadline = newPaymentDeadlineUtc;

        // 06 CK_Transactions_FreezePassive: TimeoutRemainingSeconds must be
        // NULL whenever TimeoutFrozenAt is NULL. The freeze/resume happy-path
        // (T50) is responsible for stamping TimeoutRemainingSeconds during
        // freeze and clearing it on resume; this path only re-issues Hangfire
        // jobs against an already-cleared remainder so we leave the column
        // alone.
        var paymentJobId = _scheduler.Schedule<ITimeoutExecutor>(
            x => x.ExecutePaymentTimeoutAsync(transaction.Id),
            remaining);
        transaction.PaymentTimeoutJobId = paymentJobId;

        var warningRatio = await ReadWarningRatioAsync(cancellationToken);
        string? warningJobId = null;
        if (warningRatio is { } ratio && ratio > 0m && ratio < 1m && transaction.TimeoutWarningSentAt is null)
        {
            var warningDelay = TimeSpan.FromTicks((long)(remaining.Ticks * (double)ratio));
            if (warningDelay < TimeSpan.Zero) warningDelay = TimeSpan.Zero;
            warningJobId = _scheduler.Schedule<IWarningDispatcher>(
                x => x.DispatchWarningAsync(transaction.Id),
                warningDelay);
            transaction.TimeoutWarningJobId = warningJobId;
        }
        else
        {
            // Either no ratio configured or warning already sent — do not
            // re-schedule a duplicate warning (09 §13.3 "çift uyarı engeli").
            transaction.TimeoutWarningJobId = null;
        }

        return new TimeoutJobIds(paymentJobId, warningJobId);
    }

    private async Task<Transaction> LoadAsync(Guid transactionId, CancellationToken ct)
    {
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted, ct);
        return transaction
            ?? throw new InvalidOperationException(
                $"Transaction {transactionId} not found for timeout scheduling.");
    }

    private async Task<decimal?> ReadWarningRatioAsync(CancellationToken ct)
    {
        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == WarningRatioKey && s.IsConfigured)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
