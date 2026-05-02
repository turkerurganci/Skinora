using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Persistence;
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
/// Scope per 05 §4.4: AcceptDeadline, TradeOfferToSellerDeadline,
/// TradeOfferToBuyerDeadline are scanner-driven; PaymentDeadline is normally
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
    private readonly TimeoutSchedulingOptions _options;
    private readonly ILogger<DeadlineScannerJob> _logger;

    public DeadlineScannerJob(
        AppDbContext db,
        IBackgroundJobScheduler scheduler,
        TimeProvider clock,
        IOptions<TimeoutSchedulingOptions> options,
        ILogger<DeadlineScannerJob> logger)
    {
        _db = db;
        _scheduler = scheduler;
        _clock = clock;
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
                            || (t.Status == TransactionStatus.TRADE_OFFER_SENT_TO_SELLER && t.TradeOfferToSellerDeadline != null && t.TradeOfferToSellerDeadline < now)
                            || (t.Status == TransactionStatus.ITEM_ESCROWED && t.PaymentDeadline != null && t.PaymentDeadline < now)
                            || (t.Status == TransactionStatus.TRADE_OFFER_SENT_TO_BUYER && t.TradeOfferToBuyerDeadline != null && t.TradeOfferToBuyerDeadline < now)
                        ))
            .Take(_options.DeadlineScannerBatchSize)
            .ToListAsync();

        if (candidates.Count == 0) return;

        foreach (var transaction in candidates)
        {
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
        }

        await _db.SaveChangesAsync();
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
