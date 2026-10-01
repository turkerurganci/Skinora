using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Settlement;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// Operational tuning for <see cref="DeliveryPollingJob"/>, bound from the
/// <c>DeliveryPolling</c> configuration section. Infrastructure knobs, not
/// business parameters — they trade detection latency against Steam Community
/// reads (08 §2.6), so they live in configuration like
/// <c>TimeoutSchedulingOptions</c>.
/// </summary>
public sealed class DeliveryPollingOptions
{
    public const string SectionName = "DeliveryPolling";

    /// <summary>
    /// Off switch. The poll spends reads from the same Community budget the
    /// seller's listing, the readiness check and the timeout round use; an
    /// operator facing a 429 storm can stop it without a deploy.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Transactions examined per run (the job runs every minute). Default 1.</summary>
    public int BatchSize { get; set; } = 1;

    /// <summary>Minimum seconds between two polls of the same transaction. Default 600 (10 min).</summary>
    public int RecheckSeconds { get; set; } = 600;
}

/// <summary>What one run did — returned for tests and logged.</summary>
public sealed record DeliveryPollingRunSummary(int Examined, int Delivered, int Detected);

public interface IDeliveryPollingJob
{
    Task<DeliveryPollingRunSummary> ExecuteAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// P2P-DeliveryPollingJob (owner decision 2026-10-02) — looks for a delivery
/// BEFORE the deadline, so a passive buyer's transaction does not have to wait
/// for the deadline round (02 §9.2: until now the inventory was read only at
/// buyer confirmation, dispute opening and the deadline).
/// </summary>
/// <remarks>
/// <para>
/// <b>What it acts on.</b> Only evidence observed TOGETHER in one round: the
/// seller's asset gone AND the buyer's count of that class above the baseline.
/// Partial observations are not written — accumulating them across rounds is
/// how two unrelated reads could add up to a delivery, and a poll runs far more
/// rounds than the deadline does. With the launch gate closed (DEPLOY_RUNBOOK
/// §H) the evidence is recorded with its capture and the buyer is asked to
/// confirm (<see cref="DeliveryDetectedEvent"/>); with it open the transaction
/// moves to <c>ITEM_DELIVERED</c> through the same transition the timeout round
/// uses. A misdelivery signature is left to the dispute and deadline rounds:
/// before the deadline the buyer side may simply not have caught up yet.
/// </para>
/// <para>
/// <b>Budget.</b> Steam's Community endpoint 429s after ~15–18 quick requests
/// even at 4–7.5/min (T122, re-measured 2026-10-02), so the poll asks the
/// cheapest question first: is the asset still with the seller? While it is,
/// that one read is the whole round. Only when it has left does the full
/// engine run (seller again + buyer). Each transaction is polled at most every
/// <see cref="DeliveryPollingOptions.RecheckSeconds"/>, at most
/// <see cref="DeliveryPollingOptions.BatchSize"/> per minute, and a run stops
/// at the first unreadable seller inventory rather than spending more reads
/// into an outage.
/// </para>
/// <para>
/// <b>Scope.</b> <c>PAYMENT_RECEIVED</c>, deadline still ahead (overdue rows
/// belong to the deadline scanner), a buyer baseline captured (without one no
/// inventory evidence exists — 02 §9.2), evidence not already sufficient, no
/// emergency hold, timeouts not frozen. <c>DeliveryPolledAt</c> is stamped on
/// every examined row, before any read, so a row that fails steps aside.
/// </para>
/// </remarks>
public sealed class DeliveryPollingJob : IDeliveryPollingJob
{
    public const string RecurringJobId = "delivery-polling";

    /// <summary>Every minute; the per-transaction rhythm is <see cref="DeliveryPollingOptions.RecheckSeconds"/>.</summary>
    public const string Cron = "* * * * *";

    /// <summary>
    /// The recorded evidence values that leave a delivery to discover: nothing,
    /// or one inventory half on its own. <c>BUYER_CONFIRMED</c> or both halves
    /// already settle it. Spelled out rather than masked in SQL so the filter is
    /// a plain IN over the int column.
    /// </summary>
    private static readonly DeliveryEvidence[] PollableEvidence =
    [
        DeliveryEvidence.NONE,
        DeliveryEvidence.INVENTORY_DELTA,
        DeliveryEvidence.SELLER_ASSET_GONE,
    ];

    private readonly AppDbContext _db;
    private readonly ISteamInventoryReader _inventory;
    private readonly IDeliveryVerificationService _verification;
    private readonly ISettlementSettingsProvider _settlementSettings;
    private readonly IOutboxService _outbox;
    private readonly DeliveryPollingOptions _options;
    private readonly ILogger<DeliveryPollingJob> _logger;
    private readonly TimeProvider _clock;

    public DeliveryPollingJob(
        AppDbContext db,
        ISteamInventoryReader inventory,
        IDeliveryVerificationService verification,
        ISettlementSettingsProvider settlementSettings,
        IOutboxService outbox,
        IOptions<DeliveryPollingOptions> options,
        ILogger<DeliveryPollingJob> logger,
        TimeProvider clock)
    {
        _db = db;
        _inventory = inventory;
        _verification = verification;
        _settlementSettings = settlementSettings;
        _outbox = outbox;
        _options = options.Value;
        _logger = logger;
        _clock = clock;
    }

    /// <summary>Hangfire entry point.</summary>
    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();

    public async Task<DeliveryPollingRunSummary> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || _options.BatchSize <= 0)
            return new DeliveryPollingRunSummary(0, 0, 0);

        var now = _clock.GetUtcNow().UtcDateTime;
        var recheckBefore = now.AddSeconds(-_options.RecheckSeconds);

        var dueIds = await Pollable(now, recheckBefore)
            // Never-polled first, then least recently polled, then the payment
            // that has waited longest — the same fairness rule the deadline
            // scanner applies to DeliveryRoundAt.
            .OrderBy(t => t.DeliveryPolledAt == null ? 0 : 1)
            .ThenBy(t => t.DeliveryPolledAt)
            .ThenBy(t => t.PaymentReceivedAt)
            .Select(t => t.Id)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        int examined = 0, delivered = 0, detected = 0;
        foreach (var id in dueIds)
        {
            // Each row is loaded on its own turn, tracked and re-checked. A lost
            // concurrent update clears the whole change tracker below; rows
            // loaded up front would then be detached, and the next round's
            // flags would silently not be saved while its capture and outbox
            // event were — the buyer asked again on every poll.
            var transaction = await Pollable(now, recheckBefore)
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (transaction is null) continue;

            examined++;
            PollOutcome outcome;
            try
            {
                outcome = await PollAsync(transaction, now, cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // The buyer confirmed, a dispute round ran, or the deadline
                // scanner moved the row while Steam was being read. Drop this
                // round's writes — stamp, flags, capture and outbox event
                // together — and let the next run see the row as it is now.
                _db.ChangeTracker.Clear();
                _logger.LogInformation(ex,
                    "Delivery poll for transaction {TransactionId} lost a concurrent update — "
                    + "nothing from this round was saved; the next run re-reads it",
                    transaction.Id);
                continue;
            }

            if (outcome == PollOutcome.Delivered) delivered++;
            if (outcome == PollOutcome.Detected) detected++;
            if (outcome == PollOutcome.SellerUnreadable)
            {
                // Steam is limiting or down. Further rows this minute would
                // only spend reads into the same wall.
                break;
            }
        }

        if (examined > 0)
        {
            _logger.LogInformation(
                "Delivery poll: {Examined} examined, {Delivered} delivered, {Detected} detected "
                + "behind the launch gate (02 §9.2)",
                examined, delivered, detected);
        }

        return new DeliveryPollingRunSummary(examined, delivered, detected);
    }

    /// <summary>The rows the poll may look at now (see the class remarks for each condition).</summary>
    private IQueryable<Transaction> Pollable(DateTime now, DateTime recheckBefore) =>
        _db.Set<Transaction>()
            .Where(t => !t.IsDeleted
                        && !t.IsOnHold
                        && t.TimeoutFrozenAt == null
                        && t.Status == TransactionStatus.PAYMENT_RECEIVED
                        && t.DeliveryDeadline != null
                        && t.DeliveryDeadline > now
                        && t.BuyerId != null
                        && t.BuyerBaselineCapturedAt != null
                        && t.BuyerBaselineClassCount != null
                        && PollableEvidence.Contains(t.DeliveryEvidence)
                        && (t.DeliveryPolledAt == null || t.DeliveryPolledAt <= recheckBefore));

    private async Task<PollOutcome> PollAsync(
        Transaction transaction, DateTime now, CancellationToken cancellationToken)
    {
        // Stamped before any read: a row whose round fails must not jump back
        // to the head of the queue on the next minute.
        transaction.DeliveryPolledAt = now;

        // The cheapest question first. Until the asset has left the seller no
        // delivery can have happened, and this one read is the whole round.
        // Fresh: the sidecar's 120 s cache would hide a trade made a minute ago.
        var sellerSteamId = await SteamIdOfAsync(transaction.SellerId, cancellationToken);
        var seller = sellerSteamId is null
            ? InventoryLookupResult.Unavailable
            : await _inventory.GetItemAsync(
                sellerSteamId, transaction.ItemAssetId, InventoryReadFreshness.Fresh, cancellationToken);

        if (seller.Visibility != InventoryVisibility.Public)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return PollOutcome.SellerUnreadable;
        }

        if (seller.Item is not null)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return PollOutcome.NothingYet;
        }

        // The asset has left. Now the full 02 §9.2 engine: seller again and the
        // buyer against the baseline, both fresh.
        var result = await _verification.VerifyAsync(
            transaction, InventoryReadFreshness.Fresh, cancellationToken);

        // Same-round evidence only (see the class remarks). The engine ORs in
        // whatever was recorded before; the poll does not lean on that.
        if (!result.ObservedEvidence.IsSufficientForDelivery())
        {
            await _db.SaveChangesAsync(cancellationToken);
            return PollOutcome.NothingYet;
        }

        transaction.DeliveryEvidence = result.Evidence;

        if (result.Verdict == DeliveryVerdict.Delivered)
        {
            // The launch gate is open: the platform's own inference releases
            // the transaction, exactly as the timeout round would.
            var transition = new InventoryDeliveryTransition(_db, _settlementSettings, _outbox, _logger);
            var moved = await transition.TryDeliverAsync(transaction, result, now, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            if (!moved) return PollOutcome.NothingYet;

            _logger.LogInformation(
                "Transaction {TransactionId}: delivery poll observed the item reach the buyer "
                + "before the deadline — PAYMENT_RECEIVED → ITEM_DELIVERED (02 §9.2)",
                transaction.Id);
            return PollOutcome.Delivered;
        }

        // InventoryEvidencePendingReview — the gate is closed. Record what was
        // seen (DEPLOY_RUNBOOK §H.3 reads these rows, and an early observation
        // is what makes its B1 latency figure mean something) and ask the buyer,
        // whose own confirmation the gate does not hold.
        DeliveryEvidenceCaptureRecorder.Record(_db, transaction, result, now);
        await _outbox.PublishAsync(
            new DeliveryDetectedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                BuyerId: transaction.BuyerId!.Value,
                ItemName: transaction.ItemName,
                OccurredAt: now),
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Transaction {TransactionId}: delivery poll observed the item reach the buyer; the "
            + "launch gate is closed, so the evidence is recorded and the buyer is asked to "
            + "confirm (DEPLOY_RUNBOOK §H)",
            transaction.Id);
        return PollOutcome.Detected;
    }

    private async Task<string?> SteamIdOfAsync(Guid userId, CancellationToken cancellationToken)
    {
        var steamId = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SteamId)
            .FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(steamId) ? null : steamId;
    }

    private enum PollOutcome
    {
        NothingYet,
        SellerUnreadable,
        Detected,
        Delivered,
    }
}
