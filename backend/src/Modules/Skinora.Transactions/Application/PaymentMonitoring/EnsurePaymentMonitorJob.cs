using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Webhooks;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.PaymentMonitoring;

/// <summary>
/// What the reconciler decided for one <c>ACTIVE</c> deposit address.
/// </summary>
public enum PaymentMonitorAction
{
    /// <summary>The buyer's payment window is open and the deposit can still
    /// receive money — the sidecar must be watching this address.</summary>
    Arm,

    /// <summary>The window is closed — stop the sidecar and stamp the row
    /// <c>STOPPED</c>.</summary>
    Disarm,

    /// <summary>The address exists but its window has not opened yet
    /// (allocated at CREATED, revealed at SELLER_CONFIRMED) — leave both the
    /// sidecar and the row alone.</summary>
    Idle,
}

/// <summary>
/// Hangfire recurring job that reconciles the sidecar's in-memory active
/// monitor set against the database (T139). The database is the source of
/// truth; the sidecar registry is a cache that can be lost at any time.
/// </summary>
/// <remarks>
/// <para>
/// This job is the reason T139 does not need a startup-only recovery hook
/// like <c>PostCancelMonitorRecoveryHook</c>. Post-cancel windows are 24h to
/// 30 days, so replaying them once per backend start is enough. An active
/// payment window is 30-120 minutes: a monitor lost to a <em>sidecar</em>
/// restart (which no backend hook observes) would let the buyer's transfer
/// land unseen and the transaction time out with the money already on-chain.
/// A per-minute sweep closes backend restart, sidecar restart and a dropped
/// outbox delivery with one mechanism.
/// </para>
/// <para>
/// Re-arming is safe to do unconditionally: <c>MonitorRegistry.start</c> is
/// idempotent per address and a duplicate call keeps the existing pagination
/// cursors and dedup set (08 §3.4), so a re-arm never re-emits an already
/// delivered payment event.
/// </para>
/// <para>
/// The disarm half is what keeps <c>MonitoringStatus</c> honest. Before T139
/// the allocator was the only writer of <c>ACTIVE</c> and the cancel pipeline
/// the only path out of it, so a happy-path row stayed <c>ACTIVE</c> forever
/// and <c>ReconciliationService</c> (which snapshots every non-<c>STOPPED</c>
/// address) grew without bound.
/// </para>
/// </remarks>
public sealed class EnsurePaymentMonitorJob
{
    public const string RecurringJobId = "ensure-payment-monitor";

    /// <summary>
    /// Cron — every minute. Mirrors <see cref="EnsurePaymentAddressJob.Cron"/>:
    /// the two jobs are the same shape of defence (allocate the address vs arm
    /// its monitor) and a minute is well inside the shortest payment window.
    /// </summary>
    public const string Cron = "* * * * *";

    /// <summary>
    /// Maximum addresses processed per run. Only <em>actionable</em> rows are
    /// fetched (see <see cref="ActionableStates"/>), so idle allocations from
    /// CREATED/ACCEPTED transactions can never crowd armed windows out of the
    /// batch.
    /// </summary>
    public const int BatchSize = 200;

    /// <summary>
    /// The payment window is open: the deposit address has been revealed to
    /// the buyer (02 §2.2 step 3) and can still legitimately receive money.
    /// <c>PAYMENT_RECEIVED</c> and <c>ITEM_DELIVERED</c> stay armed on purpose
    /// — 02 §4.4 (overpayment refund) and 03 §5.5 (a second transfer after a
    /// complete one) both describe transfers that arrive AFTER the payment was
    /// accepted, and neither can be seen by a monitor that stopped at
    /// confirmation.
    /// </summary>
    public static readonly TransactionStatus[] ArmedStates =
    [
        TransactionStatus.SELLER_CONFIRMED,
        TransactionStatus.PAYMENT_RECEIVED,
        TransactionStatus.ITEM_DELIVERED,
    ];

    /// <summary>
    /// Terminal states — nothing the platform would act on can arrive at the
    /// deposit address any more. The CANCELLED_* rows normally leave
    /// <c>ACTIVE</c> through <c>PostCancelMonitorStarter</c> (which hands the
    /// address to the gradual cadence) and so never reach this job; they are
    /// listed anyway because a cancel that ran without a PaymentAddress row
    /// would otherwise leave a later-allocated monitor armed forever.
    /// </summary>
    public static readonly TransactionStatus[] WindowClosedStates =
    [
        TransactionStatus.COMPLETED,
        TransactionStatus.REFUNDED,
        TransactionStatus.CANCELLED_TIMEOUT,
        TransactionStatus.CANCELLED_SELLER,
        TransactionStatus.CANCELLED_BUYER,
        TransactionStatus.CANCELLED_ADMIN,
    ];

    /// <summary>
    /// The address is allocated but the buyer has not been shown it yet, so
    /// there is nothing to watch. <c>FLAGGED</c> belongs here rather than in
    /// <see cref="WindowClosedStates"/>: a flagged transaction can be approved
    /// by an admin and resume, and stamping it <c>STOPPED</c> would make that
    /// resume unmonitorable.
    /// </summary>
    public static readonly TransactionStatus[] WindowNotOpenStates =
    [
        TransactionStatus.CREATED,
        TransactionStatus.ACCEPTED,
        TransactionStatus.FLAGGED,
    ];

    /// <summary>
    /// Statuses worth fetching — the union of armed and closed. Applied in SQL
    /// so the batch is never filled with <see cref="WindowNotOpenStates"/> rows.
    /// </summary>
    public static readonly TransactionStatus[] ActionableStates =
        [.. ArmedStates, .. WindowClosedStates];

    private readonly AppDbContext _db;
    private readonly IBlockchainSidecarClient _sidecar;
    private readonly ILogger<EnsurePaymentMonitorJob> _logger;

    public EnsurePaymentMonitorJob(
        AppDbContext db,
        IBlockchainSidecarClient sidecar,
        ILogger<EnsurePaymentMonitorJob> logger)
    {
        _db = db;
        _sidecar = sidecar;
        _logger = logger;
    }

    /// <summary>
    /// Decide what should happen to one deposit address. Pure, so the decision
    /// table can be tested without a database or a sidecar.
    /// </summary>
    /// <param name="status">Owning transaction's status.</param>
    /// <param name="depositSwept">Whether a <c>SWEEP</c> ledger row for this
    /// address has reached <c>CONFIRMED</c> — the deposit has been emptied into
    /// the hot wallet (05 §3.3), so the window is over even though the
    /// transaction may still be short of terminal.</param>
    public static PaymentMonitorAction Classify(TransactionStatus status, bool depositSwept)
    {
        if (WindowClosedStates.Contains(status)) return PaymentMonitorAction.Disarm;
        if (WindowNotOpenStates.Contains(status)) return PaymentMonitorAction.Idle;

        // Armed states only from here. The sweep gate is checked last because
        // it can only fire inside ITEM_DELIVERED (SweepQueueJob's own state
        // gate), and a swept deposit holds nothing left to refund.
        if (depositSwept) return PaymentMonitorAction.Disarm;
        return PaymentMonitorAction.Arm;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await _db.Set<PaymentAddress>()
            .Where(p => !p.IsDeleted
                && p.MonitoringStatus == MonitoringStatus.ACTIVE
                && ActionableStates.Contains(p.Transaction.Status))
            .OrderBy(p => p.CreatedAt)
            .Take(BatchSize)
            .Select(p => new CandidateRow
            {
                Address = p,
                Status = p.Transaction.Status,
                DepositSwept = p.BlockchainTransactions.Any(b =>
                    b.Type == BlockchainTransactionType.SWEEP
                    && b.Status == BlockchainTransactionStatus.CONFIRMED),
            })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0) return;

        int armed = 0, disarmed = 0, failed = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var action = Classify(candidate.Status, candidate.DepositSwept);
            if (action == PaymentMonitorAction.Idle) continue;

            if (action == PaymentMonitorAction.Arm)
            {
                var contract = KnownStablecoinContracts.ResolveContractAddress(
                    candidate.Address.ExpectedToken);
                var startStatus = await _sidecar.StartMonitoringAsync(
                    new PaymentMonitorStartRequest(
                        Address: candidate.Address.Address,
                        PaymentAddressId: candidate.Address.Id,
                        TransactionId: candidate.Address.TransactionId,
                        ExpectedContract: contract,
                        ExpectedSymbol: candidate.Address.ExpectedToken.ToString()),
                    cancellationToken);

                if (startStatus == BlockchainSidecarStatus.Success)
                {
                    armed++;
                }
                else
                {
                    failed++;
                    _logger.LogWarning(
                        "EnsurePaymentMonitorJob: could not arm PaymentAddress {Id} (status={Status}) — retrying next run.",
                        candidate.Address.Id, startStatus);
                }

                continue;
            }

            var stopStatus = await _sidecar.StopMonitoringAsync(
                candidate.Address.Address, cancellationToken);

            if (stopStatus == BlockchainSidecarStatus.Success)
            {
                // Stamp only on an acknowledged stop. Writing STOPPED while the
                // sidecar is unreachable would leave a monitor running that no
                // query can find again — the row would drop out of this job's
                // candidate set and out of ReconciliationService's scope at the
                // same time.
                candidate.Address.MonitoringStatus = MonitoringStatus.STOPPED;
                candidate.Address.MonitoringExpiresAt = null;
                disarmed++;
            }
            else
            {
                failed++;
                _logger.LogWarning(
                    "EnsurePaymentMonitorJob: could not disarm PaymentAddress {Id} (status={Status}) — row stays ACTIVE, retrying next run.",
                    candidate.Address.Id, stopStatus);
            }
        }

        if (disarmed > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (armed > 0 || disarmed > 0 || failed > 0)
        {
            _logger.LogInformation(
                "EnsurePaymentMonitorJob complete: candidates={Total} armed={Armed} disarmed={Disarmed} failed={Failed}",
                candidates.Count, armed, disarmed, failed);
        }
    }

    // Hangfire serializes Expression<Action<T>> so the entry-point exposes a
    // synchronous wrapper. The job body itself runs async on the worker.
    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();

    private sealed class CandidateRow
    {
        public required PaymentAddress Address { get; init; }
        public required TransactionStatus Status { get; init; }
        public required bool DepositSwept { get; init; }
    }
}
