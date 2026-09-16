using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Application.UserSuspension;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Application.Reputation;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Reputation;

/// <inheritdoc cref="INonDeliveryAbuseEvaluator"/>
/// <remarks>
/// <para>
/// <b>What counts as a non-delivery event</b> — the seller-fault delivery
/// family <see cref="ReputationAggregator"/> already charges to the seller, so
/// the two consumers cannot disagree about who failed:
/// </para>
/// <list type="bullet">
///   <item>the delivery window expiring: <c>PAYMENT_RECEIVED → CANCELLED_TIMEOUT</c>;</item>
///   <item>the seller cancelling after payment: <c>PAYMENT_RECEIVED → CANCELLED_SELLER</c> (03 §2.5 step 8);</item>
///   <item>the seller taking the item back after delivery: <c>REFUNDED</c> with <c>DeliveryReversedAt</c> set.</item>
/// </list>
/// <para>
/// Excluded, for the reasons the aggregator gives: a delivery timeout that ran
/// because an admin ruled on a misdelivery dispute
/// (<c>TimeoutReleasedByAdminRulingAt</c>), one the BUYER's Steam account made
/// impossible (<c>TimeoutBlockedByCounterpartyAt</c>, 08 §2.2a), admin
/// cancellations, and dispute refunds (a <c>REFUNDED</c> row without
/// <c>DeliveryReversedAt</c> is a platform ruling, not an observed reversal).
/// </para>
/// <para>
/// No emergency-hold cascade on the flag: 02 §14.0 reserves that for sanctions
/// matches and account takeover, and freezing the seller's unrelated open
/// transactions would punish buyers who have nothing to do with the pattern.
/// Suspension already closes the fund-flow mutations.
/// </para>
/// </remarks>
public sealed class NonDeliveryAbuseEvaluator : INonDeliveryAbuseEvaluator
{
    /// <summary><c>flagDetail.pattern</c> of the staged <c>ABNORMAL_BEHAVIOR</c> flag (07 §9.3).</summary>
    public const string FlagPattern = "NON_DELIVERY_REPEAT";

    private readonly AppDbContext _db;
    private readonly INonDeliveryAbuseThresholdsProvider _thresholds;
    private readonly IAccountFlagChecker _flagChecker;
    private readonly ITransactionFraudFlagWriter _flagWriter;
    private readonly IUserSuspensionWriter _suspensionWriter;
    private readonly TimeProvider _clock;
    private readonly ILogger<NonDeliveryAbuseEvaluator> _logger;

    public NonDeliveryAbuseEvaluator(
        AppDbContext db,
        INonDeliveryAbuseThresholdsProvider thresholds,
        IAccountFlagChecker flagChecker,
        ITransactionFraudFlagWriter flagWriter,
        IUserSuspensionWriter suspensionWriter,
        TimeProvider clock,
        ILogger<NonDeliveryAbuseEvaluator> logger)
    {
        _db = db;
        _thresholds = thresholds;
        _flagChecker = flagChecker;
        _flagWriter = flagWriter;
        _suspensionWriter = suspensionWriter;
        _clock = clock;
        _logger = logger;
    }

    public async Task<NonDeliveryAbuseOutcome> EvaluateAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var trigger = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.Id == transactionId)
            .Select(t => new { t.SellerId, t.Status })
            .FirstOrDefaultAsync(cancellationToken);

        // Cheap pre-filter: every other terminal status (COMPLETED above all)
        // is answered without reading settings or history.
        if (trigger is null || trigger.Status is not (TransactionStatus.CANCELLED_TIMEOUT
                or TransactionStatus.CANCELLED_SELLER
                or TransactionStatus.REFUNDED))
        {
            return new NonDeliveryAbuseOutcome(NonDeliveryAbuseAction.NotANonDeliveryEvent, 0);
        }

        var thresholds = await _thresholds.GetAsync(cancellationToken);
        if (!thresholds.IsEnabled)
            return new NonDeliveryAbuseOutcome(NonDeliveryAbuseAction.RuleDisabled, 0);

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var windowStart = nowUtc - TimeSpan.FromDays(thresholds.WindowDays);
        var events = await LoadEventsAsync(trigger.SellerId, windowStart, cancellationToken);

        // The trigger qualifies by membership in the same list that is counted,
        // so "is this a non-delivery event" and "what do we count" can never be
        // two definitions.
        if (!events.Any(e => e.TransactionId == transactionId))
            return new NonDeliveryAbuseOutcome(NonDeliveryAbuseAction.NotANonDeliveryEvent, 0);

        var count = events.Count;

        if (count >= thresholds.SuspendCount)
            return await SuspendAsync(trigger.SellerId, events, thresholds, nowUtc, cancellationToken);

        if (count >= thresholds.FlagCount)
        {
            var staged = await StageFlagIfNonePendingAsync(
                trigger.SellerId, events, thresholds, suspended: false, cancellationToken);
            return new NonDeliveryAbuseOutcome(
                staged ? NonDeliveryAbuseAction.Flagged : NonDeliveryAbuseAction.FlagAlreadyPending,
                count);
        }

        return new NonDeliveryAbuseOutcome(NonDeliveryAbuseAction.BelowThreshold, count);
    }

    private async Task<NonDeliveryAbuseOutcome> SuspendAsync(
        Guid sellerId,
        IReadOnlyList<NonDeliveryEvent> events,
        NonDeliveryAbuseThresholds thresholds,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Tracked on purpose: a second qualifying transaction of the same
        // seller evaluated in the same unit of work gets this instance back
        // from the identity map and sees IsSuspended already true.
        var seller = await _db.Set<User>()
            .FirstOrDefaultAsync(u => u.Id == sellerId && !u.IsDeleted, cancellationToken);
        if (seller is null)
        {
            _logger.LogWarning(
                "Non-delivery sanction skipped — seller {SellerId} not found ({Count} events in {WindowDays} days)",
                sellerId, events.Count, thresholds.WindowDays);
            return new NonDeliveryAbuseOutcome(NonDeliveryAbuseAction.NotANonDeliveryEvent, events.Count);
        }

        if (seller.IsSuspended)
            return new NonDeliveryAbuseOutcome(NonDeliveryAbuseAction.AlreadySuspended, events.Count);

        await _suspensionWriter.StageSuspensionAsync(
            seller,
            reason: string.Format(
                CultureInfo.InvariantCulture,
                "Otomatik askı: son {0} gün içinde {1} işlemde ödeme alındıktan sonra item teslim edilmedi. "
                + "Askı, admin incelemesiyle kaldırılır.",
                thresholds.WindowDays, events.Count),
            expiresAt: null,
            actorId: SeedConstants.SystemUserId,
            actorType: ActorType.SYSTEM,
            ipAddress: null,
            nowUtc: nowUtc,
            cancellationToken);

        // The flag carries the evidence (which transactions, which kind): the
        // suspension alone would leave the reviewing admin with a reason text
        // and nothing to open. Normally one is already pending from the flag
        // threshold; it is missing when both events landed in one batch or an
        // admin already closed the earlier one.
        await StageFlagIfNonePendingAsync(sellerId, events, thresholds, suspended: true, cancellationToken);

        _logger.LogWarning(
            "Seller {SellerId} automatically suspended — {Count} non-delivery events in {WindowDays} days (02 §14.2)",
            sellerId, events.Count, thresholds.WindowDays);

        return new NonDeliveryAbuseOutcome(NonDeliveryAbuseAction.Suspended, events.Count);
    }

    private async Task<bool> StageFlagIfNonePendingAsync(
        Guid sellerId,
        IReadOnlyList<NonDeliveryEvent> events,
        NonDeliveryAbuseThresholds thresholds,
        bool suspended,
        CancellationToken cancellationToken)
    {
        // A pending flag is already in front of an admin; a second one for the
        // same pattern would only split the review.
        if (await _flagChecker.HasPendingAccountFlagAsync(
                sellerId, FraudFlagType.ABNORMAL_BEHAVIOR, cancellationToken))
        {
            return false;
        }

        await _flagWriter.StageAccountFlagAsync(
            sellerId,
            FraudFlagType.ABNORMAL_BEHAVIOR,
            // Shape mirrors AbnormalBehaviorFlagDetail (07 §9.3) — the admin
            // flag screen renders pattern + description for this type.
            JsonSerializer.Serialize(new
            {
                pattern = FlagPattern,
                description = Describe(events, thresholds, suspended),
            }),
            cancellationToken);
        return true;
    }

    private static string Describe(
        IReadOnlyList<NonDeliveryEvent> events,
        NonDeliveryAbuseThresholds thresholds,
        bool suspended)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"Satıcı son {thresholds.WindowDays} gün içinde {events.Count} işlemde ödeme alındıktan sonra teslim etmedi "
            + $"(eşikler: flag {thresholds.FlagCount}, askı {thresholds.SuspendCount}).");
        if (suspended)
            text.Append(" Hesap otomatik olarak askıya alındı.");
        text.Append(" İşlemler: ");
        text.Append(string.Join("; ", events
            .OrderBy(e => e.OccurredAt)
            .Select(e => string.Format(
                CultureInfo.InvariantCulture,
                "{0} ({1}, {2:yyyy-MM-dd})",
                e.TransactionId,
                e.Kind switch
                {
                    NonDeliveryKind.DeliveryTimeout => "teslimat süresi doldu",
                    NonDeliveryKind.SellerCancelAfterPayment => "ödeme sonrası satıcı iptali",
                    _ => "teslimattan sonra geri alma",
                },
                e.OccurredAt))));
        text.Append('.');
        return text.ToString();
    }

    private async Task<IReadOnlyList<NonDeliveryEvent>> LoadEventsAsync(
        Guid sellerId,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        var rows = await _db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => t.SellerId == sellerId
                        && ((t.Status == TransactionStatus.CANCELLED_SELLER
                                && t.CancelledAt != null && t.CancelledAt >= windowStart)
                            || (t.Status == TransactionStatus.CANCELLED_TIMEOUT
                                && t.CancelledAt != null && t.CancelledAt >= windowStart
                                && t.TimeoutReleasedByAdminRulingAt == null
                                && t.TimeoutBlockedByCounterpartyAt == null)
                            || (t.Status == TransactionStatus.REFUNDED
                                && t.DeliveryReversedAt != null && t.DeliveryReversedAt >= windowStart)))
            .Select(t => new { t.Id, t.Status, t.CancelledAt, t.DeliveryReversedAt })
            .ToListAsync(cancellationToken);

        var cancellationIds = rows
            .Where(r => r.Status != TransactionStatus.REFUNDED)
            .Select(r => r.Id)
            .ToList();

        // The history row is the only record of which phase a cancellation left
        // (06 §3.6) — the same source the aggregator and the cooldown evaluator
        // attribute fault from.
        var previousStatusByTx = cancellationIds.Count == 0
            ? new Dictionary<Guid, TransactionStatus>()
            : await _db.Set<TransactionHistory>()
                .AsNoTracking()
                .Where(h => cancellationIds.Contains(h.TransactionId)
                            && (h.NewStatus == TransactionStatus.CANCELLED_SELLER
                                || h.NewStatus == TransactionStatus.CANCELLED_TIMEOUT)
                            && h.PreviousStatus != null)
                .GroupBy(h => h.TransactionId)
                .Select(g => new
                {
                    TxId = g.Key,
                    PreviousStatus = g.OrderByDescending(h => h.CreatedAt).First().PreviousStatus!.Value,
                })
                .ToDictionaryAsync(x => x.TxId, x => x.PreviousStatus, cancellationToken);

        var events = new List<NonDeliveryEvent>();
        foreach (var row in rows)
        {
            if (row.Status == TransactionStatus.REFUNDED)
            {
                events.Add(new NonDeliveryEvent(row.Id, NonDeliveryKind.DeliveryReversed, row.DeliveryReversedAt!.Value));
                continue;
            }

            if (!previousStatusByTx.TryGetValue(row.Id, out var previous)
                || previous != TransactionStatus.PAYMENT_RECEIVED)
            {
                continue;
            }

            events.Add(new NonDeliveryEvent(
                row.Id,
                row.Status == TransactionStatus.CANCELLED_TIMEOUT
                    ? NonDeliveryKind.DeliveryTimeout
                    : NonDeliveryKind.SellerCancelAfterPayment,
                row.CancelledAt!.Value));
        }

        return events;
    }

    private enum NonDeliveryKind
    {
        DeliveryTimeout,
        SellerCancelAfterPayment,
        DeliveryReversed,
    }

    private sealed record NonDeliveryEvent(Guid TransactionId, NonDeliveryKind Kind, DateTime OccurredAt);
}
