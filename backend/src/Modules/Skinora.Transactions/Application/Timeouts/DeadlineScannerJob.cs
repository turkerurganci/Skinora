using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly TimeoutSchedulingOptions _options;
    private readonly ILogger<DeadlineScannerJob> _logger;

    public DeadlineScannerJob(
        AppDbContext db,
        IBackgroundJobScheduler scheduler,
        TimeProvider clock,
        ITimeoutSideEffectPublisher sideEffects,
        IPostCancelMonitorStarter postCancelMonitor,
        ITransactionReputationRefresher reputation,
        IOptions<TimeoutSchedulingOptions> options,
        ILogger<DeadlineScannerJob> logger)
    {
        _db = db;
        _scheduler = scheduler;
        _clock = clock;
        _sideEffects = sideEffects;
        _postCancelMonitor = postCancelMonitor;
        _reputation = reputation;
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

    private async Task ScanBatchAsync()
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        // Single query covers all four phase deadlines. Filtered to honor the
        // 09 §13.3 guards (IsOnHold + TimeoutFrozenAt) inside SQL so frozen and
        // emergency-held rows never even reach the in-memory pass.
        var candidates = await _db.Set<Transaction>()
            .Where(t => !t.IsDeleted
                        && !t.IsOnHold
                        && t.TimeoutFrozenAt == null
                        && (
                            (t.Status == TransactionStatus.CREATED && t.AcceptDeadline != null && t.AcceptDeadline < now)
                            || (t.Status == TransactionStatus.ACCEPTED && t.SellerConfirmDeadline != null && t.SellerConfirmDeadline < now)
                            || (t.Status == TransactionStatus.SELLER_CONFIRMED && t.PaymentDeadline != null && t.PaymentDeadline < now)
                            || (t.Status == TransactionStatus.PAYMENT_RECEIVED && t.DeliveryDeadline != null && t.DeliveryDeadline < now)
                        ))
            .Take(_options.DeadlineScannerBatchSize)
            .ToListAsync();

        if (candidates.Count == 0) return;

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

        if (affected.Count == 0) return;

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
