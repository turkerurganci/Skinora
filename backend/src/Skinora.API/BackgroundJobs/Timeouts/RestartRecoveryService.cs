using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.API.BackgroundJobs.Timeouts;

/// <summary>
/// Default <see cref="IRestartRecoveryService"/> — extends active phase
/// deadlines by the detected outage window and re-issues the per-transaction
/// Hangfire jobs for ITEM_ESCROWED rows (05 §4.4).
/// </summary>
/// <remarks>
/// <para>
/// The recovery pass runs once at startup, before
/// <see cref="TimeoutSchedulerStartupHook"/> primes the heartbeat and scanner
/// chains. Frozen transactions (<c>TimeoutFrozenAt != null</c> or
/// <c>IsOnHold</c>) are skipped — their resume is owned by T50.
/// </para>
/// <para>
/// The implementation lives in Skinora.API rather than in
/// Skinora.Transactions because it crosses module boundaries
/// (<c>SystemHeartbeats</c> + <c>Transaction</c>) and depends on
/// <see cref="ITimeoutSchedulingService"/> for re-scheduling, mirroring the
/// pattern used by <c>OutboxStartupHook</c>.
/// </para>
/// </remarks>
public sealed class RestartRecoveryService : IRestartRecoveryService
{
    private readonly AppDbContext _db;
    private readonly ITimeoutSchedulingService _scheduling;
    private readonly TimeProvider _clock;
    private readonly TimeoutSchedulingOptions _options;
    private readonly ILogger<RestartRecoveryService> _logger;

    public RestartRecoveryService(
        AppDbContext db,
        ITimeoutSchedulingService scheduling,
        TimeProvider clock,
        IOptions<TimeoutSchedulingOptions> options,
        ILogger<RestartRecoveryService> logger)
    {
        _db = db;
        _scheduling = scheduling;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RestartRecoveryResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var heartbeat = await _db.Set<SystemHeartbeat>()
            .FirstOrDefaultAsync(h => h.Id == SeedConstants.SystemHeartbeatId, cancellationToken);
        if (heartbeat is null)
        {
            _logger.LogWarning("SystemHeartbeats singleton missing — skipping restart recovery.");
            return new RestartRecoveryResult(TimeSpan.Zero, false, 0, 0);
        }

        var outage = now - heartbeat.LastHeartbeat;
        if (outage < TimeSpan.FromSeconds(_options.RecoveryThresholdSeconds))
        {
            // Below threshold — fresh restart, no skew. Stamp now and exit.
            heartbeat.LastHeartbeat = now;
            heartbeat.UpdatedAt = now;
            await _db.SaveChangesAsync(cancellationToken);
            return new RestartRecoveryResult(outage, false, 0, 0);
        }

        var activeStates = new[]
        {
            TransactionStatus.CREATED,
            TransactionStatus.TRADE_OFFER_SENT_TO_SELLER,
            TransactionStatus.ITEM_ESCROWED,
            TransactionStatus.TRADE_OFFER_SENT_TO_BUYER,
        };

        var actives = await _db.Set<Transaction>()
            .Where(t => !t.IsDeleted
                        && !t.IsOnHold
                        && t.TimeoutFrozenAt == null
                        && activeStates.Contains(t.Status))
            .ToListAsync(cancellationToken);

        var rescheduledPaymentJobs = 0;
        foreach (var transaction in actives)
        {
            if (transaction.AcceptDeadline.HasValue)
                transaction.AcceptDeadline = transaction.AcceptDeadline.Value + outage;
            if (transaction.TradeOfferToSellerDeadline.HasValue)
                transaction.TradeOfferToSellerDeadline = transaction.TradeOfferToSellerDeadline.Value + outage;
            if (transaction.PaymentDeadline.HasValue)
                transaction.PaymentDeadline = transaction.PaymentDeadline.Value + outage;
            if (transaction.TradeOfferToBuyerDeadline.HasValue)
                transaction.TradeOfferToBuyerDeadline = transaction.TradeOfferToBuyerDeadline.Value + outage;
        }

        // Persist deadline extensions before re-issuing the per-tx Hangfire
        // jobs so the re-scheduled job sees the new PaymentDeadline value when
        // it later calls back into the executor.
        if (actives.Count > 0) await _db.SaveChangesAsync(cancellationToken);

        foreach (var transaction in actives.Where(t => t.Status == TransactionStatus.ITEM_ESCROWED))
        {
            if (!transaction.PaymentDeadline.HasValue) continue;
            var remaining = transaction.PaymentDeadline.Value - now;
            try
            {
                await _scheduling.ReschedulePaymentTimeoutAsync(
                    transaction.Id,
                    remaining,
                    transaction.PaymentDeadline.Value,
                    cancellationToken);
                rescheduledPaymentJobs++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not re-issue payment timeout for transaction {TransactionId}.",
                    transaction.Id);
            }
        }

        if (rescheduledPaymentJobs > 0) await _db.SaveChangesAsync(cancellationToken);

        heartbeat.LastHeartbeat = now;
        heartbeat.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Restart recovery extended {ExtendedCount} transaction deadline(s) by {OutageSeconds}s "
            + "and rescheduled {ReschedCount} payment job(s).",
            actives.Count, (long)outage.TotalSeconds, rescheduledPaymentJobs);

        return new RestartRecoveryResult(outage, true, actives.Count, rescheduledPaymentJobs);
    }
}
