using Skinora.Platform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Delivery;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Application.PostCancel;
using Skinora.Transactions.Application.Reputation;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Transactions.Application.Timeouts;

/// <summary>
/// Default <see cref="IDeadlineScannerJob"/> — self-rescheduling Hangfire job
/// (09 §13.4) that scans phase deadlines and fires <c>Timeout</c> on overdue
/// transactions.
/// </summary>
/// <remarks>
/// <para>
/// Scope per 05 §4.4: AcceptDeadline, SellerConfirmDeadline,
/// DeliveryDeadline are scanner-driven; PaymentDeadline is normally
/// driven by the per-tx Hangfire delayed job (09 §13.3) but is also included
/// here as a belt-and-suspenders fallback for orphan-job scenarios (atomicity
/// gap between Hangfire write and DB commit).
/// </para>
/// <para>
/// T127: the delivery phase consumes again, but only through
/// <see cref="IDeliveryTimeoutRound"/> — 05 §4.4 requires a verification round
/// before a delivery timeout may cancel, and that round can also conclude the
/// opposite (deliver) or refuse to conclude at all. The T124 gate that reported
/// these rows without touching them is gone.
/// </para>
/// <para>
/// The reschedule is wrapped in a <c>try/finally</c> so a batch error never
/// breaks the chain (mirrors <c>OutboxDispatcher</c>, 09 §13.4).
/// </para>
/// </remarks>
public sealed class DeadlineScannerJob : IDeadlineScannerJob
{
    private readonly AppDbContext _db;
    private readonly IBackgroundJobScheduler _scheduler;
    private readonly TimeProvider _clock;
    private readonly ITimeoutSideEffectPublisher _sideEffects;
    private readonly IPostCancelMonitorStarter _postCancelMonitor;
    private readonly ITransactionReputationRefresher _reputation;
    private readonly IDeliveryTimeoutRound _deliveryRound;
    private readonly IWarningDispatcher _warnings;
    private readonly TimeoutSchedulingOptions _options;
    private readonly ILogger<DeadlineScannerJob> _logger;

    public DeadlineScannerJob(
        AppDbContext db,
        IBackgroundJobScheduler scheduler,
        TimeProvider clock,
        ITimeoutSideEffectPublisher sideEffects,
        IPostCancelMonitorStarter postCancelMonitor,
        ITransactionReputationRefresher reputation,
        IDeliveryTimeoutRound deliveryRound,
        IWarningDispatcher warnings,
        IOptions<TimeoutSchedulingOptions> options,
        ILogger<DeadlineScannerJob> logger)
    {
        _db = db;
        _scheduler = scheduler;
        _clock = clock;
        _sideEffects = sideEffects;
        _postCancelMonitor = postCancelMonitor;
        _reputation = reputation;
        _deliveryRound = deliveryRound;
        _warnings = warnings;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ScanAndRescheduleAsync()
    {
        try
        {
            await ScanBatchAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deadline scanner iteration failed.");
        }
        finally
        {
            try
            {
                _scheduler.Schedule<IDeadlineScannerJob>(
                    j => j.ScanAndRescheduleAsync(),
                    TimeSpan.FromSeconds(_options.DeadlineScannerIntervalSeconds));
            }
            catch (Exception scheduleEx)
            {
                _logger.LogCritical(
                    scheduleEx,
                    "Deadline scanner could not reschedule itself — chain broken until restart.");
            }
        }
    }

    /// <summary>
    /// WP7 — fires the delivery-phase timeout warning for rows that have
    /// crossed the warning ratio and not been warned yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ratio is the same <c>timeout_warning_ratio</c> the payment phase
    /// uses (02 §16.2), applied to the delivery window. Ratio unset or out of
    /// the open (0,1) range disables the warning, matching
    /// <c>TimeoutSchedulingService</c> — an operator who turns it off turns it
    /// off everywhere.
    /// </para>
    /// <para>
    /// Idempotency comes from <c>TimeoutWarningSentAt</c>, which the dispatcher
    /// stamps; without it every sweep would re-warn the same seller. The
    /// column already existed for the payment phase, so no migration.
    /// </para>
    /// <para>
    /// A throw here must not take the cancellation sweep down with it: a
    /// missed warning is a smaller failure than a missed timeout.
    /// </para>
    /// </remarks>
    private async Task DispatchDeliveryWarningsAsync(DateTime now)
    {
        var ratio = await ReadWarningRatioAsync();
        if (ratio is not { } r || r <= 0m || r >= 1m) return;

        var due = await _db.Set<Transaction>()
            .Where(t => !t.IsDeleted
                        && !t.IsOnHold
                        && t.TimeoutFrozenAt == null
                        && t.Status == TransactionStatus.PAYMENT_RECEIVED
                        && t.DeliveryDeadline != null
                        && t.DeliveryDeadline > now
                        && t.TimeoutWarningSentAt == null)
            .Select(t => t.Id)
            .Take(_options.DeadlineScannerBatchSize)
            .ToListAsync();

        foreach (var id in due)
        {
            try
            {
                await _warnings.DispatchWarningAsync(id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Delivery timeout warning failed for transaction {TransactionId} — "
                    + "the cancellation sweep continues.",
                    id);
            }
        }
    }

    private async Task<decimal?> ReadWarningRatioAsync()
    {
        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(x => x.Key == "timeout_warning_ratio" && x.IsConfigured)
            .Select(x => x.Value)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return decimal.TryParse(
            raw,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }
    private async Task ScanBatchAsync()
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        // Single query covers the three phases whose deadline decides on its
        // own. Filtered to honor the 09 §13.3 guards (IsOnHold +
        // TimeoutFrozenAt) inside SQL so frozen and emergency-held rows never
        // even reach the in-memory pass. The delivery phase is queried
        // separately below — its expiry decides nothing by itself.
        var candidates = await _db.Set<Transaction>()
            .Where(t => !t.IsDeleted
                        && !t.IsOnHold
                        && t.TimeoutFrozenAt == null
                        && (
                            (t.Status == TransactionStatus.CREATED && t.AcceptDeadline != null && t.AcceptDeadline < now)
                            || (t.Status == TransactionStatus.ACCEPTED && t.SellerConfirmDeadline != null && t.SellerConfirmDeadline < now)
                            || (t.Status == TransactionStatus.SELLER_CONFIRMED && t.PaymentDeadline != null && t.PaymentDeadline < now)
                        ))
            .Take(_options.DeadlineScannerBatchSize)
            .ToListAsync();

        // WP7 (P2P-DeliveryTimeoutWarning) — warn before cancelling.
        // Scanner-driven rather than a delayed job, because 05 §4.4 makes the
        // whole delivery phase scanner-driven; arming a Hangfire job here
        // would give the phase a second, independent executor — exactly what
        // ArmDeliveryDeadlineAsync deliberately avoids.
        await DispatchDeliveryWarningsAsync(now);

        var deliveryCancellations = await RunDeliveryTimeoutRoundsAsync(now);
        candidates.AddRange(deliveryCancellations);

        // WP15 — collect the parties of every transaction that actually timed
        // out so reputation/cooldown can be recomputed after the batch flush.
        var affected = new List<(Guid SellerId, Guid? BuyerId)>();

        foreach (var transaction in candidates)
        {
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
                    "Scanner refused to fire Timeout on transaction {TransactionId} ({ErrorCode}).",
                    transaction.Id, ex.ErrorCode);
                continue;
            }

            await _sideEffects.PublishAsync(transaction, previousStatus);

            // T75 — post-cancel monitor stamp. The starter is idempotent on
            // missing PaymentAddress (CREATED / ACCEPTED
            // timeouts that never allocated a deposit address).
            var cancelledAt = transaction.CancelledAt ?? _clock.GetUtcNow().UtcDateTime;
            await _postCancelMonitor.RequestStartAsync(
                transaction.Id, cancelledAt, CancellationToken.None);

            // WP15 — audit-trail row (06 §3.6). PreviousStatus is the only source
            // the reputation/cooldown responsibility map reads to attribute the
            // timeout to the at-fault party (06 §3.1).
            TransactionHistoryRecorder.Record(
                _db, transaction, previousStatus, TransactionTrigger.Timeout,
                ActorType.SYSTEM, SeedConstants.SystemUserId, cancelledAt);
            affected.Add((transaction.SellerId, transaction.BuyerId));
        }

        // Not `affected.Count == 0`: a delivery round can leave real work
        // behind — an ITEM_DELIVERED transition, an evidence capture, a raised
        // dispute — without any party timing out. Asking the change tracker is
        // both narrower (it sees exactly what this scope mutated) and safer
        // than a bookkeeping flag that a new arm could forget to set.
        if (affected.Count == 0 && !_db.ChangeTracker.HasChanges()) return;

        // WP15 — flush every timeout transition + history row, then recompute
        // reputation/cooldown for all affected parties. The aggregator/cooldown
        // read AsNoTracking and resolve timeout responsibility from the freshly
        // written history rows, so the flips must be committed-in-transaction
        // first. One DB transaction wraps the whole batch (09 §13.3).
        await using var dbTx = await _db.Database.BeginTransactionAsync();
        await _db.SaveChangesAsync();
        foreach (var (sellerId, buyerId) in affected)
            await _reputation.RefreshAsync(sellerId, buyerId, evaluateCooldown: true, CancellationToken.None);
        await _db.SaveChangesAsync();
        await dbTx.CommitAsync();
    }

    /// <summary>
    /// T127 — run the 05 §4.4 verification round on every overdue delivery and
    /// return the transactions that earned a cancellation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a separate query.</b> The T124 acceptance criterion asks for the
    /// <c>PAYMENT_RECEIVED</c> branch back in the consuming query, and the
    /// substance of that — the branch consumes again — is delivered here. The
    /// query stays separate because the hazard T124 named survives this task:
    /// an overdue delivery does not always resolve. Three of the five verdicts
    /// leave the row in <c>PAYMENT_RECEIVED</c> and permanently overdue (held
    /// for launch-gate review, escalated to an admin, or unreadable), and with
    /// the launch gate closed at launch the first of those is the EXPECTED
    /// outcome of a delivery the buyer never confirmed. Sharing
    /// <c>DeadlineScannerBatchSize</c> with a growing set of permanently overdue
    /// rows would let them crowd out the accept / seller-confirm / payment
    /// phases — a silent availability failure, which is exactly the reasoning
    /// recorded in T124 decision (a).
    /// </para>
    /// <para>
    /// <b>Why the separate cap.</b> A round costs up to two rate-limited Steam
    /// reads (08 §2.2), so the delivery phase gets its own, much smaller budget
    /// per pass.
    /// </para>
    /// <para>
    /// <b>Why the window is fair rather than oldest-first</b> (T127 validation
    /// finding B2). The same three verdicts that keep the row overdue keep it in
    /// this query: nothing in the round moves <c>DeliveryDeadline</c> or the
    /// status. Ordered by deadline, those rows are the OLDEST ones, so they sit
    /// at the head of the window permanently — and once there are
    /// <see cref="TimeoutSchedulingOptions.DeliveryVerificationBatchSize"/> of
    /// them, no delivery that expires afterwards is ever examined again. That is
    /// the T124 starvation moved into the delivery phase rather than removed.
    /// So the window is ordered by when each row was last EXAMINED, nulls first:
    /// a never-examined delivery outranks every held one, and a held row cannot
    /// come back until <c>DeliveryRoundRecheckSeconds</c> has passed. The
    /// re-check interval is what keeps the held rows alive — an unreadable
    /// inventory can become readable, and 08 §2.3 does not let the platform
    /// treat "cannot see" as settled — while spending the Steam budget on the
    /// rows that still have a question open.
    /// </para>
    /// <para>
    /// A round that throws costs its own transaction and nothing else: one
    /// unreachable sidecar must not stop the cancellations the other phases owe.
    /// Its <c>DeliveryRoundAt</c> stamp is written by the round itself before any
    /// arm runs, so a row that throws every pass still yields its slot instead of
    /// consuming the whole budget.
    /// </para>
    /// </remarks>
    private async Task<List<Transaction>> RunDeliveryTimeoutRoundsAsync(DateTime now)
    {
        var recheckBefore = now.AddSeconds(-_options.DeliveryRoundRecheckSeconds);

        var overdue = await _db.Set<Transaction>()
            .Where(t => !t.IsDeleted
                        && !t.IsOnHold
                        && t.TimeoutFrozenAt == null
                        && t.Status == TransactionStatus.PAYMENT_RECEIVED
                        && t.DeliveryDeadline != null
                        && t.DeliveryDeadline < now
                        && (t.DeliveryRoundAt == null || t.DeliveryRoundAt <= recheckBefore))
            // Nulls first, then least-recently-examined, then oldest deadline.
            // The null rank is projected explicitly rather than left to
            // ORDER BY DeliveryRoundAt: SQL Server happens to sort NULL first on
            // ASC, but "a never-examined delivery outranks every held one" is the
            // rule this query exists for, and a rule that load-bearing should not
            // rest on a provider's collation default.
            .OrderBy(t => t.DeliveryRoundAt == null ? 0 : 1)
            .ThenBy(t => t.DeliveryRoundAt)
            .ThenBy(t => t.DeliveryDeadline)
            .Take(_options.DeliveryVerificationBatchSize)
            .ToListAsync();

        var cancellations = new List<Transaction>();
        foreach (var transaction in overdue)
        {
            try
            {
                var decision = await _deliveryRound.RunAsync(transaction, CancellationToken.None);
                if (decision == DeliveryTimeoutDecision.Cancel)
                    cancellations.Add(transaction);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Delivery timeout round failed for transaction {TransactionId} — "
                    + "left in PAYMENT_RECEIVED and retried on the next scan.",
                    transaction.Id);
            }
        }

        return cancellations;
    }
}

/// <summary>
/// Operational tuning for T47 timeout scheduling. Bound from the
/// <c>Timeouts</c> configuration section. These are infrastructure knobs
/// (poll interval, recovery threshold) rather than business parameters, so
/// they live in <c>appsettings.json</c> alongside <c>HangfireOptions</c> and
/// <c>OutboxOptions</c> instead of in <c>SystemSettings</c>.
/// </summary>
public sealed class TimeoutSchedulingOptions
{
    public const string SectionName = "Timeouts";

    /// <summary>How often the deadline scanner self-reschedules. Default 30 seconds (05 §4.4).</summary>
    public int DeadlineScannerIntervalSeconds { get; set; } = 30;

    /// <summary>Maximum transactions processed per scanner iteration.</summary>
    public int DeadlineScannerBatchSize { get; set; } = 200;

    /// <summary>
    /// Maximum delivery-verification rounds per scanner iteration (T127).
    /// </summary>
    /// <remarks>
    /// Much smaller than <see cref="DeadlineScannerBatchSize"/> because a round
    /// is not a state check but up to two rate-limited Steam inventory reads
    /// (08 §2.2). A backlog drains over successive passes in deadline order
    /// rather than saturating the sidecar queue in one.
    /// </remarks>
    public int DeliveryVerificationBatchSize { get; set; } = 20;

    /// <summary>
    /// How long a transaction that has already had a delivery-verification
    /// round waits before it may have another (T127). Default 15 minutes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only re-examinations are throttled: a delivery whose deadline has just
    /// passed carries a null <c>DeliveryRoundAt</c> and is examined on the very
    /// next pass, so the timeout stays as prompt as the scan interval.
    /// </para>
    /// <para>
    /// The interval exists because three of the five verdicts leave the row
    /// overdue and eligible forever. Without it those rows would re-enter the
    /// window every 30 seconds and, being the oldest, would consume the whole
    /// per-pass budget. Fifteen minutes is short enough that a Steam inventory
    /// that becomes readable is picked up within the hour and long enough that a
    /// backlog of held rows costs a bounded number of reads.
    /// </para>
    /// </remarks>
    public int DeliveryRoundRecheckSeconds { get; set; } = 900;

    /// <summary>How often the heartbeat self-reschedules. Default 30 seconds (05 §4.4).</summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Minimum outage window (current time minus last heartbeat) that triggers
    /// the restart-recovery deadline extension. Defaults to twice the
    /// heartbeat interval to absorb a single missed beat without false-positive
    /// extensions.
    /// </summary>
    public int RecoveryThresholdSeconds { get; set; } = 60;
}
