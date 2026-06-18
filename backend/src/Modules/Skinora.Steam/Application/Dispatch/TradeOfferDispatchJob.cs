using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Steam.Application.BotSelection;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;
using Skinora.Users.Domain.Entities;

namespace Skinora.Steam.Application.Dispatch;

/// <summary>
/// Per-minute Hangfire job that drives the orphaned trade-offer dispatch
/// transitions (T106a — formalises T69-K1). Two legs:
/// <list type="bullet">
///   <item><b>Escrow</b> — <c>ACCEPTED</c> → select the lowest-load ACTIVE bot
///   (<see cref="IBotSelectionService"/>, 06 §3.10), persist
///   <c>Transaction.EscrowBotId</c>, fire <c>SendTradeOfferToSeller</c>, and
///   ask the sidecar to send a <c>SELLER_TO_BOT</c> offer requesting the
///   seller's item.</item>
///   <item><b>Delivery</b> — <c>PAYMENT_RECEIVED</c> → reuse the escrow bot,
///   fire <c>SendTradeOfferToBuyer</c>, and ask the sidecar to send a
///   <c>BOT_TO_BUYER</c> offer delivering the escrowed item.</item>
/// </list>
///
/// <para>
/// Idempotency: a transaction is a candidate only while it is in the leg's
/// source state AND has no <c>TradeOffer</c> row for that direction. On success
/// the state flips (self-excluding next tick); on a terminal sidecar failure a
/// FAILED <c>TradeOffer</c> row is recorded (blocking re-dispatch) and
/// <see cref="TradeOfferDispatchFailedEvent"/> is published. A transient failure
/// (sidecar unreachable / 5xx) records nothing and is retried next tick — the
/// per-minute cadence is the backoff, bounded by the leg's timeout deadline.
/// </para>
/// </summary>
public sealed class TradeOfferDispatchJob
{
    public const string RecurringJobId = "trade-offer-dispatch";

    /// <summary>Cron — every minute. Mirrors <c>OutgoingTransferDispatchJob.Cron</c>.</summary>
    public const string Cron = "* * * * *";

    /// <summary>Maximum transactions processed per leg per tick.</summary>
    public const int BatchSize = 20;

    private readonly AppDbContext _db;
    private readonly IBotSelectionService _botSelection;
    private readonly ITradeOfferDispatchClient _client;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<TradeOfferDispatchJob> _logger;

    public TradeOfferDispatchJob(
        AppDbContext db,
        IBotSelectionService botSelection,
        ITradeOfferDispatchClient client,
        IOutboxService outbox,
        TimeProvider clock,
        ILogger<TradeOfferDispatchJob> logger)
    {
        _db = db;
        _botSelection = botSelection;
        _client = client;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var escrowIds = await _db.Set<Transaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted
                && t.Status == TransactionStatus.ACCEPTED
                && !_db.Set<TradeOffer>().Any(o =>
                    o.TransactionId == t.Id && o.Direction == TradeOfferDirection.TO_SELLER))
            .OrderBy(t => t.CreatedAt)
            .Take(BatchSize)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var deliveryIds = await _db.Set<Transaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted
                && t.Status == TransactionStatus.PAYMENT_RECEIVED
                && !_db.Set<TradeOffer>().Any(o =>
                    o.TransactionId == t.Id && o.Direction == TradeOfferDirection.TO_BUYER))
            .OrderBy(t => t.CreatedAt)
            .Take(BatchSize)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (escrowIds.Count == 0 && deliveryIds.Count == 0) return;

        _logger.LogInformation(
            "TradeOfferDispatchJob picked up {Escrow} escrow + {Delivery} delivery candidates",
            escrowIds.Count, deliveryIds.Count);

        foreach (var id in escrowIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchEscrowAsync(id, cancellationToken);
        }

        foreach (var id in deliveryIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchDeliveryAsync(id, cancellationToken);
        }
    }

    private async Task DispatchEscrowAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);
        if (transaction is null || transaction.Status != TransactionStatus.ACCEPTED) return;

        // Race re-validation: a sent/failed TradeOffer row may have landed since
        // the candidate scan (concurrent tick, webhook replay).
        if (await HasOfferAsync(transactionId, TradeOfferDirection.TO_SELLER, cancellationToken)) return;

        var bot = await _botSelection.SelectAsync(cancellationToken);
        if (bot is null)
        {
            // No ACTIVE capacity — transient. Leave ACCEPTED; retried next tick.
            // Bot-health lifecycle events alert admins when the pool is empty.
            _logger.LogWarning(
                "TradeOfferDispatchJob: no ACTIVE bot for escrow dispatch of transaction {TransactionId} — retrying next tick",
                transactionId);
            return;
        }

        var sellerSteamId = await ResolveSteamIdAsync(transaction.SellerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(sellerSteamId))
        {
            _logger.LogError(
                "TradeOfferDispatchJob: seller {SellerId} has no SteamId — cannot dispatch escrow for transaction {TransactionId}",
                transaction.SellerId, transactionId);
            return;
        }

        var request = new TradeOfferDispatchRequest(
            TransactionId: transaction.Id,
            Direction: TradeOfferDispatchDirection.SellerToBot,
            PartnerSteamId: sellerSteamId,
            Items: [new TradeOfferDispatchItem(transaction.ItemAssetId, SteamConstants.Cs2AppId, SteamConstants.Cs2ContextId)],
            BotAccountName: bot.DisplayName);

        var result = await _client.SendAsync(request, cancellationToken);
        var fromStatus = transaction.Status;
        switch (result.Status)
        {
            case TradeOfferDispatchStatus.Sent:
            case TradeOfferDispatchStatus.Pending:
                transaction.EscrowBotId = bot.Id;
                FireForward(transaction, TransactionTrigger.SendTradeOfferToSeller);
                await PublishStatusChangedAsync(transaction, fromStatus, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Escrow dispatch OK — transaction {TransactionId} → TRADE_OFFER_SENT_TO_SELLER via bot {BotId} (offer {OfferId})",
                    transactionId, bot.Id, result.OfferId);
                return;

            case TradeOfferDispatchStatus.Failed:
                await RecordDispatchFailureAsync(transaction, TradeOfferDirection.TO_SELLER, bot.Id, result, cancellationToken);
                return;

            case TradeOfferDispatchStatus.Unavailable:
            default:
                _logger.LogWarning(
                    "Escrow dispatch transient failure for transaction {TransactionId} ({Reason}) — retrying next tick",
                    transactionId, result.Reason);
                return;
        }
    }

    private async Task DispatchDeliveryAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);
        if (transaction is null || transaction.Status != TransactionStatus.PAYMENT_RECEIVED) return;

        if (await HasOfferAsync(transactionId, TradeOfferDirection.TO_BUYER, cancellationToken)) return;

        if (transaction.EscrowBotId is not { } escrowBotId || escrowBotId == Guid.Empty)
        {
            _logger.LogError(
                "TradeOfferDispatchJob: transaction {TransactionId} has no EscrowBotId — cannot dispatch delivery",
                transactionId);
            return;
        }
        if (string.IsNullOrWhiteSpace(transaction.EscrowBotAssetId))
        {
            _logger.LogError(
                "TradeOfferDispatchJob: transaction {TransactionId} has no EscrowBotAssetId — cannot dispatch delivery",
                transactionId);
            return;
        }
        if (transaction.BuyerId is not { } buyerId)
        {
            _logger.LogError(
                "TradeOfferDispatchJob: transaction {TransactionId} has no BuyerId — cannot dispatch delivery",
                transactionId);
            return;
        }

        var botAccountName = await _db.Set<PlatformSteamBot>()
            .Where(b => b.Id == escrowBotId)
            .Select(b => b.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(botAccountName))
        {
            _logger.LogError(
                "TradeOfferDispatchJob: escrow bot {BotId} for transaction {TransactionId} not found — cannot dispatch delivery",
                escrowBotId, transactionId);
            return;
        }

        var buyerSteamId = await ResolveSteamIdAsync(buyerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(buyerSteamId))
        {
            _logger.LogError(
                "TradeOfferDispatchJob: buyer {BuyerId} has no SteamId — cannot dispatch delivery for transaction {TransactionId}",
                buyerId, transactionId);
            return;
        }

        var request = new TradeOfferDispatchRequest(
            TransactionId: transaction.Id,
            Direction: TradeOfferDispatchDirection.BotToBuyer,
            PartnerSteamId: buyerSteamId,
            Items: [new TradeOfferDispatchItem(transaction.EscrowBotAssetId!, SteamConstants.Cs2AppId, SteamConstants.Cs2ContextId)],
            BotAccountName: botAccountName);

        var result = await _client.SendAsync(request, cancellationToken);
        var fromStatus = transaction.Status;
        switch (result.Status)
        {
            case TradeOfferDispatchStatus.Sent:
            case TradeOfferDispatchStatus.Pending:
                FireForward(transaction, TransactionTrigger.SendTradeOfferToBuyer);
                await PublishStatusChangedAsync(transaction, fromStatus, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Delivery dispatch OK — transaction {TransactionId} → TRADE_OFFER_SENT_TO_BUYER via bot {BotId} (offer {OfferId})",
                    transactionId, escrowBotId, result.OfferId);
                return;

            case TradeOfferDispatchStatus.Failed:
                await RecordDispatchFailureAsync(transaction, TradeOfferDirection.TO_BUYER, escrowBotId, result, cancellationToken);
                return;

            case TradeOfferDispatchStatus.Unavailable:
            default:
                _logger.LogWarning(
                    "Delivery dispatch transient failure for transaction {TransactionId} ({Reason}) — retrying next tick",
                    transactionId, result.Reason);
                return;
        }
    }

    private Task<bool> HasOfferAsync(Guid transactionId, TradeOfferDirection direction, CancellationToken cancellationToken)
        => _db.Set<TradeOffer>().AnyAsync(
            o => o.TransactionId == transactionId && o.Direction == direction, cancellationToken);

    private Task<string?> ResolveSteamIdAsync(Guid userId, CancellationToken cancellationToken)
        => _db.Set<User>()
            .Where(u => u.Id == userId)
            .Select(u => u.SteamId)
            .FirstOrDefaultAsync(cancellationToken);

    private static void FireForward(Transaction transaction, TransactionTrigger trigger)
    {
        var machine = new TransactionStateMachine(transaction);
        machine.Fire(trigger);
    }

    // WP9 — stage the RT1 TransactionStatusChanged push (07 §11.1) on the same
    // outbox/SaveChanges as the dispatch transition. FromStatus is captured by
    // the caller before the Fire(); ToStatus is read post-Fire. Best-effort: the
    // realtime consumer swallows transport errors so a missed push never blocks.
    private Task PublishStatusChangedAsync(
        Transaction transaction, TransactionStatus fromStatus, CancellationToken cancellationToken)
        => _outbox.PublishAsync(
            new TransactionStatusChangedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                FromStatus: fromStatus,
                ToStatus: transaction.Status,
                OccurredAt: _clock.GetUtcNow().UtcDateTime),
            cancellationToken);

    private async Task RecordDispatchFailureAsync(
        Transaction transaction, TradeOfferDirection direction, Guid botId,
        TradeOfferDispatchResult result, CancellationToken cancellationToken)
    {
        _db.Set<TradeOffer>().Add(new TradeOffer
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            PlatformSteamBotId = botId,
            Direction = direction,
            SteamTradeOfferId = null,
            Status = TradeOfferStatus.FAILED,
            RetryCount = result.Attempts,
            ErrorMessage = Truncate(result.Reason, 500),
        });

        await _outbox.PublishAsync(
            new TradeOfferDispatchFailedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                Direction: direction,
                PlatformSteamBotId: botId,
                LastErrorReason: result.Reason,
                Attempts: result.Attempts,
                OccurredAt: _clock.GetUtcNow().UtcDateTime),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogError(
            "Trade-offer dispatch FAILED — transaction {TransactionId} ({Direction}) after {Attempts} attempts: {Reason}",
            transaction.Id, direction, result.Attempts, result.Reason);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }

    // Hangfire serializes Expression<Action<T>>, so the entry point exposes a
    // synchronous wrapper that delegates to the async body on the worker.
    public void Execute() => ExecuteAsync().GetAwaiter().GetResult();
}
