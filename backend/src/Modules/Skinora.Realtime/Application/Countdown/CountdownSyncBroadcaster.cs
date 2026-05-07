using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Realtime.Application.Countdown;

/// <summary>
/// Periodically broadcasts <c>CountdownSync</c> on <c>/hubs/transactions</c>
/// for every active transaction (T61 — 07 §11.1 RT1, 04 §7.3 countdown).
/// </summary>
/// <remarks>
/// <para>
/// Sweeps every <see cref="CountdownSyncOptions.Interval"/> (30s by default),
/// joins the appropriate deadline column for each transaction's current
/// status, and pushes a <see cref="TransactionRealtimePayloads.CountdownSync"/>
/// payload to the per-transaction group. Frozen transactions
/// (<c>IsOnHold</c> or any T54 fraud-flag freeze) report the snapshot
/// remaining-seconds with <c>frozen=true</c> + the originating freeze reason.
/// </para>
/// <para>
/// The broadcaster is deliberately stateless: it does not track which
/// connections are listening because SignalR groups handle that already, and
/// it does not write anywhere. A failure of one push must not poison the
/// whole sweep, so each transaction is handled in its own try/catch.
/// </para>
/// </remarks>
public sealed class CountdownSyncBroadcaster : BackgroundService
{
    private static readonly TransactionStatus[] ActiveStatuses =
    [
        TransactionStatus.CREATED,
        TransactionStatus.ACCEPTED,
        TransactionStatus.TRADE_OFFER_SENT_TO_SELLER,
        TransactionStatus.ITEM_ESCROWED,
        TransactionStatus.TRADE_OFFER_SENT_TO_BUYER,
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<CountdownSyncOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<CountdownSyncBroadcaster> _logger;

    public CountdownSyncBroadcaster(
        IServiceScopeFactory scopeFactory,
        IOptions<CountdownSyncOptions> options,
        TimeProvider clock,
        ILogger<CountdownSyncBroadcaster> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation(
                "CountdownSync broadcaster disabled by configuration; skipping sweep loop.");
            return;
        }

        var interval = _options.Value.Interval;
        if (interval <= TimeSpan.Zero)
        {
            _logger.LogWarning(
                "CountdownSync interval {Interval} is non-positive; broadcaster will not run.",
                interval);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BroadcastOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CountdownSync sweep failed; will retry after interval.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Run a single sweep. Exposed so integration tests can drive it.</summary>
    public async Task BroadcastOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<ITransactionRealtimePublisher>();

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        var snapshots = await db.Set<Transaction>()
            .AsNoTracking()
            .Where(t => ActiveStatuses.Contains(t.Status))
            .Select(t => new TransactionCountdownSnapshot(
                t.Id,
                t.Status,
                t.AcceptDeadline,
                t.TradeOfferToSellerDeadline,
                t.PaymentDeadline,
                t.TradeOfferToBuyerDeadline,
                t.IsOnHold,
                t.TimeoutFreezeReason,
                t.TimeoutRemainingSeconds))
            .ToListAsync(cancellationToken);

        foreach (var snapshot in snapshots)
        {
            try
            {
                if (TryBuildPayload(snapshot, nowUtc, out var payload))
                {
                    await publisher.PublishCountdownSyncAsync(payload!, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "CountdownSync push failed for transaction {TransactionId}; continuing sweep.",
                    snapshot.TransactionId);
            }
        }
    }

    private static bool TryBuildPayload(
        TransactionCountdownSnapshot snapshot,
        DateTime nowUtc,
        out TransactionRealtimePayloads.CountdownSync? payload)
    {
        payload = null;

        var (phase, deadline) = ResolvePhase(snapshot);
        if (phase is null)
        {
            return false;
        }

        var frozen = snapshot.IsOnHold || snapshot.TimeoutFreezeReason is not null;
        int remaining;

        if (frozen)
        {
            remaining = snapshot.TimeoutRemainingSeconds ?? ComputeRemainingSeconds(deadline, nowUtc);
        }
        else
        {
            remaining = ComputeRemainingSeconds(deadline, nowUtc);
        }

        payload = new TransactionRealtimePayloads.CountdownSync(
            TransactionId: snapshot.TransactionId,
            TimeoutType: phase.Value,
            RemainingSeconds: remaining,
            Frozen: frozen,
            FrozenReason: frozen ? snapshot.TimeoutFreezeReason : null);
        return true;
    }

    private static (TimeoutPhase? Phase, DateTime? Deadline) ResolvePhase(
        TransactionCountdownSnapshot snapshot) =>
        snapshot.Status switch
        {
            TransactionStatus.CREATED =>
                (TimeoutPhase.Accept, snapshot.AcceptDeadline),
            TransactionStatus.ACCEPTED or TransactionStatus.TRADE_OFFER_SENT_TO_SELLER =>
                (TimeoutPhase.TradeOfferToSeller, snapshot.TradeOfferToSellerDeadline),
            TransactionStatus.ITEM_ESCROWED =>
                (TimeoutPhase.Payment, snapshot.PaymentDeadline),
            TransactionStatus.TRADE_OFFER_SENT_TO_BUYER =>
                (TimeoutPhase.Delivery, snapshot.TradeOfferToBuyerDeadline),
            _ => (null, null),
        };

    private static int ComputeRemainingSeconds(DateTime? deadline, DateTime nowUtc)
    {
        if (!deadline.HasValue)
        {
            return 0;
        }
        var seconds = (deadline.Value - nowUtc).TotalSeconds;
        return seconds > 0 ? (int)Math.Floor(seconds) : 0;
    }

    private sealed record TransactionCountdownSnapshot(
        Guid TransactionId,
        TransactionStatus Status,
        DateTime? AcceptDeadline,
        DateTime? TradeOfferToSellerDeadline,
        DateTime? PaymentDeadline,
        DateTime? TradeOfferToBuyerDeadline,
        bool IsOnHold,
        TimeoutFreezeReason? TimeoutFreezeReason,
        int? TimeoutRemainingSeconds);
}
