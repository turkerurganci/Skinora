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

    /// <summary>
    /// T124 — SystemSetting key for the seller's delivery window (02 §3.1
    /// "adım 6–7"). Renamed from <c>trade_offer_buyer_timeout_minutes</c> in
    /// T123; this is its first production reader.
    /// </summary>
    public const string DeliveryTimeoutKey = "delivery_timeout_minutes";

    /// <summary>
    /// Defensive fallback for <see cref="DeliveryTimeoutKey"/>, used only when
    /// the row is unconfigured, blank, unparsable or non-positive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Practically unreachable: <c>SettingsBootstrapService</c> fail-fasts at
    /// startup on an unconfigured row and <c>SystemSettingsValidator</c>
    /// rejects a non-positive int on both the admin and the env-var path.
    /// </para>
    /// <para>
    /// The value is deliberately much higher than the runbook's 60-minute
    /// example (DEPLOY_RUNBOOK §A #6). That number is an illustration, not a
    /// measurement — T122 could not measure real delivery latency — and the
    /// runbook says the launch value must be conservatively HIGH. If this
    /// fallback is ever reached the two failure directions are not symmetric:
    /// too long merely makes the buyer wait, while too short (once T127 lets
    /// the delivery timeout cancel again) refunds the buyer and blames a
    /// seller who may well have delivered.
    /// </para>
    /// </remarks>
    public const int DefaultDeliveryTimeoutMinutes = 1440;

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
        if (transaction.Status != TransactionStatus.SELLER_CONFIRMED)
            throw new InvalidOperationException(
                $"SchedulePaymentTimeout requires SELLER_CONFIRMED, got {transaction.Status}.");
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
        if (transaction.Status != TransactionStatus.SELLER_CONFIRMED)
            throw new InvalidOperationException(
                $"ReschedulePaymentTimeout requires SELLER_CONFIRMED, got {transaction.Status}.");

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

    public async Task<DateTime> ArmDeliveryDeadlineAsync(
        Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await LoadAsync(transactionId, cancellationToken);
        if (transaction.Status != TransactionStatus.PAYMENT_RECEIVED)
            throw new InvalidOperationException(
                $"ArmDeliveryDeadline requires PAYMENT_RECEIVED, got {transaction.Status}.");

        var minutes = await ReadDeliveryTimeoutMinutesAsync(cancellationToken)
            ?? DefaultDeliveryTimeoutMinutes;

        // Anchored on now rather than on PaymentReceivedAt: the seller's window
        // starts when the platform learns the money is there (03 §3.5 step 2 —
        // the "ödeme alındı, item'ı şimdi gönder" notification rides the same
        // unit of work), and the two are the same instant on the happy path.
        var deadline = _clock.GetUtcNow().UtcDateTime + TimeSpan.FromMinutes(minutes);
        transaction.DeliveryDeadline = deadline;

        // T127 — freeze/resume phase shift. TimeoutRemainingSeconds is captured
        // once, at freeze, against the state the transaction was in THEN
        // (06 §3.5 matrix), while ResumeAsync distributes it against the state
        // it is in by resume time. Payment confirmation is not blocked by a
        // freeze — the state machine only guards on IsOnHold — so a transaction
        // frozen in SELLER_CONFIRMED can arrive here still frozen, carrying a
        // remainder that belongs to the PAYMENT window. Resume would then write
        // that leftover into DeliveryDeadline and hand the seller whatever
        // seconds were left of somebody else's clock.
        //
        // Re-capturing here is the fix the plan calls option (b). The other
        // option — refusing ConfirmPayment while frozen — would leave an
        // on-chain payment with no path forward, since nothing re-drives a
        // refused confirmation on resume.
        //
        // Harmless until T127 made it reachable in anger: the scanner skips
        // frozen rows, so the corrupted deadline was never consumed. Once the
        // delivery timeout can cancel again, a window collapsed to seconds
        // produces exactly the outcome the verification round exists to
        // prevent — refund the buyer and blame a seller who had no time to send.
        if (transaction.TimeoutFrozenAt is not null)
        {
            // CK_Transactions_FreezeActive keeps this NOT NULL while frozen, so
            // the column is overwritten rather than cleared.
            transaction.TimeoutRemainingSeconds = (int)TimeSpan.FromMinutes(minutes).TotalSeconds;
        }

        // No Hangfire job here — 05 §4.4 "Aşama ayrımı" makes the delivery
        // phase scanner-driven (DeadlineScannerJob). Arming a delayed job as
        // well would give the phase two independent executors.
        return deadline;
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

    /// <summary>
    /// T124 — read <see cref="DeliveryTimeoutKey"/>. Returns <c>null</c> for an
    /// unconfigured, blank, unparsable or non-positive value so the caller
    /// falls back to <see cref="DefaultDeliveryTimeoutMinutes"/>: a zero or
    /// negative window would arm the deadline in the past and mark the seller
    /// overdue the instant the payment lands.
    /// </summary>
    private async Task<int?> ReadDeliveryTimeoutMinutesAsync(CancellationToken ct)
    {
        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == DeliveryTimeoutKey && s.IsConfigured)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0
            ? parsed
            : null;
    }
}
