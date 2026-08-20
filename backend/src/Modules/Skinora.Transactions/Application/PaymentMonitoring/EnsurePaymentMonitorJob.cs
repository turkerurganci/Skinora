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
/// like <c>PostCancelMonitorRecoveryHook</c>. Post-cancel windows are replayed
/// once per backend start, which is enough because nothing time-critical
/// depends on them resuming within the minute. The active window is different:
/// a monitor lost to a <em>sidecar</em> restart (which no backend hook
/// observes) would let the buyer's transfer land unseen and the transaction
/// time out with the money already on-chain. A per-minute sweep closes backend
/// restart, sidecar restart and a dropped outbox delivery with one mechanism.
/// </para>
/// <para>
/// <b>How long a window stays armed (T139 decision D3, cost measured in the
/// validation round — finding N1).</b> The money-critical part of the window is
/// the buyer's payment leg, 30-120 minutes. The window this job keeps open is
/// much longer: <see cref="ArmedStates"/> includes <c>ITEM_DELIVERED</c>, and
/// the sweep that closes it cannot be queued before <c>SettlementVerifiedAt</c>
/// is stamped, which <c>payout_settlement_days</c> floors at <b>7 days</b>
/// (<c>SystemSettingsValidator.MinimumSettlementDays</c>, 02 §16.2). So each
/// deposit address is polled at the 3-second active cadence for a week or more
/// after delivery, not for the length of the payment leg. That is deliberate —
/// D3 keeps the window open so the 02 §4.4 overpayment branch and the 03 §5.5
/// second-transfer branch stay observable — but it means concurrent-monitor
/// count tracks <em>a week</em> of transaction volume rather than two hours of
/// it, and TronGrid request volume scales with that count (two query phases per
/// monitor per tick). The consequence is recorded in 08 §3.4; watch
/// <c>skinora_blockchain_active_monitors</c> against the provider's rate limit
/// before raising throughput.
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
    /// Rows fetched per database round-trip. This is a <em>page</em> size, not
    /// a cap on the run: <see cref="ExecuteAsync"/> pages until the actionable
    /// set is exhausted.
    /// </summary>
    /// <remarks>
    /// It used to be a single <c>Take(200)</c>, copied from
    /// <c>EnsurePaymentAddressJob</c>. That cap is safe there and unsafe here,
    /// and the difference is whether the candidate set <em>drains</em>:
    /// allocating an address removes the transaction from
    /// <c>EnsurePaymentAddressJob</c>'s set, but arming a monitor leaves the row
    /// <c>ACTIVE</c>, so it stays a candidate for the whole window. Combined
    /// with <c>CreatedAt</c>-ascending ordering that starved the newest windows
    /// once the set passed 200 — and by <see cref="ArmedStates"/> the set tracks
    /// a week of volume (see the class remarks), so ~29 transactions a day is
    /// enough to get there. The starved population was exactly the
    /// money-critical one: a buyer paying right now, into an address the
    /// reconciler would not revisit for days. Found in the T139 validation
    /// round (finding B1, round 2).
    /// </remarks>
    public const int PageSize = 200;

    /// <summary>
    /// Hard ceiling on the addresses one run may touch — a wedge guard so a
    /// runaway set cannot hold the Hangfire worker past its own cron, not a
    /// throughput knob. Reaching it is logged as a warning naming exactly what
    /// was left unreconciled, because a silently truncated sweep reads like a
    /// complete one.
    /// </summary>
    /// <remarks>
    /// The ceiling is deliberately far above any set the platform could serve:
    /// at 5 000 concurrent monitors the sidecar is already issuing ~3 300
    /// TronGrid queries per second at its own 3-second cadence, an order of
    /// magnitude past any plausible plan budget, so
    /// <c>T139-ActiveMonitorQuotaAlarm</c> fires long before this does. One
    /// <c>start</c> per monitor per <em>minute</em> is ~0.5% of the load the
    /// sidecar already carries for the same address, which is why paging the
    /// whole set is cheap enough to be the default.
    /// </remarks>
    public const int MaxAddressesPerRun = 5_000;

    /// <summary>
    /// Ids per handover-probe query. The probe used to see at most one page of
    /// armed addresses; now that a run can arm the whole set it has to chunk
    /// rather than hand an arbitrary-length IN list to the provider.
    /// </summary>
    private const int HandoverProbeChunkSize = 1_000;

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
    /// so the pages are never filled with <see cref="WindowNotOpenStates"/> rows.
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
        int armed = 0, disarmed = 0, failed = 0, examined = 0;
        var ceilingHit = false;
        // Addresses this run actually armed, kept so the handover check below
        // can undo an arm that raced a cancel (T139 düzeltme turu — N2).
        var armedAddresses = new List<(Guid Id, string Address)>();

        // Offset paging: the ordering is stable within a run because the disarm
        // stamps are not saved until the loop ends. A row a concurrent writer
        // moves out of ACTIVE mid-run shifts the offset and can cost one skipped
        // row — benign, the next run sees it, and it is the same snapshot
        // staleness the handover guard below already compensates for on the arm
        // side.
        IQueryable<PaymentAddress> Actionable() => _db.Set<PaymentAddress>()
            .Where(p => !p.IsDeleted
                && p.MonitoringStatus == MonitoringStatus.ACTIVE
                && ActionableStates.Contains(p.Transaction.Status))
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id);

        var stoppedAtLimit = false;

        // Page over the WHOLE actionable set. A single Take() would be a silent
        // cap on a set that does not drain (see PageSize) — every run would
        // reconcile the same oldest slice and never reach the newest windows.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = MaxAddressesPerRun - examined;
            if (remaining <= 0)
            {
                stoppedAtLimit = true;
                break;
            }

            var pageSize = Math.Min(PageSize, remaining);

            var page = await Actionable()
                .Skip(examined)
                .Take(pageSize)
                .Select(p => new CandidateRow
                {
                    Address = p,
                    Status = p.Transaction.Status,
                    DepositSwept = p.BlockchainTransactions.Any(b =>
                        b.Type == BlockchainTransactionType.SWEEP
                        && b.Status == BlockchainTransactionStatus.CONFIRMED),
                })
                .ToListAsync(cancellationToken);

            if (page.Count == 0) break;
            examined += page.Count;

            foreach (var candidate in page)
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
                        armedAddresses.Add((candidate.Address.Id, candidate.Address.Address));
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
                    // Stamp only on an acknowledged stop. Writing STOPPED while
                    // the sidecar is unreachable would leave a monitor running
                    // that no query can find again — the row would drop out of
                    // this job's candidate set and out of ReconciliationService's
                    // scope at the same time.
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

            if (page.Count < pageSize) break;
        }

        if (examined == 0) return;

        // Only warn if the ceiling actually truncated something — a set of
        // exactly MaxAddressesPerRun is fully reconciled and must not report a
        // gap it does not have.
        if (stoppedAtLimit)
        {
            ceilingHit = await Actionable().Skip(examined).AnyAsync(cancellationToken);
        }

        if (ceilingHit)
        {
            _logger.LogWarning(
                "EnsurePaymentMonitorJob stopped at the {Ceiling}-address ceiling — the actionable "
                + "ACTIVE set is larger than one run may touch, so an unknown number of deposit "
                + "addresses went unreconciled this pass. At this scale the sidecar is already past "
                + "any plausible TronGrid budget (see T139-ActiveMonitorQuotaAlarm).",
                MaxAddressesPerRun);
        }

        if (disarmed > 0)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Another writer moved one of these rows out of ACTIVE between
                // the candidate query and this save — in practice the cancel
                // pipeline stamping POST_CANCEL_24H. RowVersion (09 §10.4) turns
                // that into this exception instead of a blind overwrite, which
                // matters: overwriting a post-cancel window with STOPPED would
                // silently retire the late-payment recovery guarantee (08 §3.4).
                // The whole batch's stamping is lost, not just the contended
                // row; that is acceptable because stamping is idempotent and the
                // next run (one minute later) redoes it against fresh state.
                disarmed = 0;
                _db.ChangeTracker.Clear();
                _logger.LogWarning(
                    ex,
                    "EnsurePaymentMonitorJob: a deposit address left ACTIVE while this run was "
                    + "disarming; no STOPPED stamp was written this pass. Retrying next run.");
            }
        }

        // Handover guard (T139 düzeltme turu — N2). The candidate list is a
        // snapshot: a cancel committing after it was taken moves the row to
        // POST_CANCEL_24H and PostCancelMonitorStartDispatcher stops the active
        // monitor before registering the gradual one. An arm issued from the
        // stale snapshot after that stop resurrects the active monitor on an
        // address this job will never look at again (its filter is ACTIVE), so
        // two registries would poll it until the next sidecar restart — exactly
        // the double-registration AC4(a) exists to prevent, surviving as a race.
        // One extra query per run undoes it.
        if (armedAddresses.Count > 0)
        {
            var armedById = armedAddresses.ToDictionary(a => a.Id, a => a.Address);

            // Chunked because a run may now arm the whole set rather than one
            // page of it, and an IN list is not something to hand an arbitrary
            // count to.
            var handedOver = new List<Guid>();
            foreach (var chunk in armedById.Keys.Chunk(HandoverProbeChunkSize))
            {
                var chunkIds = chunk.ToList();
                handedOver.AddRange(await _db.Set<PaymentAddress>()
                    .AsNoTracking()
                    .Where(p => chunkIds.Contains(p.Id)
                        && p.MonitoringStatus != MonitoringStatus.ACTIVE)
                    .Select(p => p.Id)
                    .ToListAsync(cancellationToken));
            }

            foreach (var id in handedOver)
            {
                var address = armedById[id];
                var undoStatus = await _sidecar.StopMonitoringAsync(address, cancellationToken);

                armed--;
                _logger.LogWarning(
                    "EnsurePaymentMonitorJob: PaymentAddress {Id} left ACTIVE while this run was "
                    + "arming it — the active monitor was stopped again (status={Status}) so the "
                    + "post-cancel registry owns the address alone.",
                    id, undoStatus);

                if (undoStatus != BlockchainSidecarStatus.Success) failed++;
            }
        }

        if (armed > 0 || disarmed > 0 || failed > 0)
        {
            _logger.LogInformation(
                "EnsurePaymentMonitorJob complete: candidates={Total} armed={Armed} disarmed={Disarmed} failed={Failed} ceilingHit={CeilingHit}",
                examined, armed, disarmed, failed, ceilingHit);
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
