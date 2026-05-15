using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Persistence;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Steam.Application.Webhooks;

/// <summary>
/// T68 webhook dispatcher. Each invocation runs in a single
/// <see cref="AppDbContext"/> transaction so the TradeOffer upsert and the
/// state-machine flip share the same SaveChanges — a partial commit cannot
/// leave the system with an offer row but no state advance.
/// </summary>
public sealed class SteamWebhookHandler : ISteamWebhookHandler
{
    private const string DirectionEscrow = "escrow";
    private const string DirectionDelivery = "delivery";

    private readonly AppDbContext _db;
    private readonly ILogger<SteamWebhookHandler> _logger;

    public SteamWebhookHandler(AppDbContext db, ILogger<SteamWebhookHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task HandleBotEventAsync(
        SteamWebhookEnvelope<BotEventData> envelope,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Admin notification integration is forward-deferred to T96; for now
        // Serilog (with the structured correlationId/event fields) is the
        // single source of truth. Anyone tailing logs sees the lifecycle.
        _logger.LogWarning(
            "Steam bot event received: {Event} account={Account} reason={Reason} status={Status} correlationId={CorrelationId}",
            envelope.Event,
            envelope.Data?.AccountName,
            envelope.Data?.Reason,
            envelope.Data?.Status,
            correlationId);
        return Task.CompletedTask;
    }

    public async Task<TradeWebhookResult> HandleTradeEventAsync(
        SteamWebhookEnvelope<TradeOfferEventData> envelope,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var data = envelope.Data
            ?? throw new BusinessRuleException("WEBHOOK_PAYLOAD_MISSING", "Trade event payload missing.");
        var eventName = envelope.Event;

        return eventName switch
        {
            SteamWebhookEvents.TradeOfferSent => await HandleSentAsync(data, correlationId, cancellationToken),
            SteamWebhookEvents.TradeOfferFailed => await HandleFailedAsync(data, correlationId, cancellationToken),
            SteamWebhookEvents.TradeOfferAccepted => await HandleAcceptedAsync(data, correlationId, cancellationToken),
            SteamWebhookEvents.TradeOfferDeclined => await HandleDeclinedAsync(data, correlationId, cancellationToken),
            SteamWebhookEvents.TradeOfferExpired => await HandleExpiredAsync(data, correlationId, cancellationToken),
            SteamWebhookEvents.TradeOfferCountered => await HandleCancellingAsync(data, correlationId, "COUNTERED", cancellationToken),
            SteamWebhookEvents.TradeOfferInvalidItems => await HandleCancellingAsync(data, correlationId, "INVALID_ITEMS", cancellationToken),
            _ => throw new BusinessRuleException("WEBHOOK_EVENT_UNKNOWN", $"Unknown trade event '{eventName}'."),
        };
    }

    private async Task<TradeWebhookResult> HandleSentAsync(
        TradeOfferEventData data, string correlationId, CancellationToken cancellationToken)
    {
        var transactionId = RequireTransactionId(data, SteamWebhookEvents.TradeOfferSent);
        var offerId = RequireOfferId(data, SteamWebhookEvents.TradeOfferSent);
        var direction = ParseDirection(data, SteamWebhookEvents.TradeOfferSent);
        var botAccountName = RequireBotAccountName(data, SteamWebhookEvents.TradeOfferSent);

        var transaction = await _db.Set<Transaction>().FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);
        if (transaction is null)
        {
            _logger.LogWarning(
                "trade_offer.sent: transactionId {TransactionId} not found — acknowledging without action. correlationId={CorrelationId}",
                transactionId, correlationId);
            return TradeWebhookResult.Unknown;
        }

        var bot = await _db.Set<PlatformSteamBot>().FirstOrDefaultAsync(b => b.DisplayName == botAccountName, cancellationToken);
        if (bot is null)
        {
            _logger.LogWarning(
                "trade_offer.sent: bot accountName={Account} not in PlatformSteamBots — acknowledging without action. correlationId={CorrelationId}",
                botAccountName, correlationId);
            return TradeWebhookResult.Unknown;
        }

        // Idempotency: same offerId already persisted → no-op (DB UQ also rejects).
        var existing = await _db.Set<TradeOffer>().FirstOrDefaultAsync(t => t.SteamTradeOfferId == offerId, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "trade_offer.sent: offer {OfferId} already recorded — idempotent replay. correlationId={CorrelationId}",
                offerId, correlationId);
            return TradeWebhookResult.Idempotent;
        }

        var tradeOffer = new TradeOffer
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            PlatformSteamBotId = bot.Id,
            Direction = direction,
            SteamTradeOfferId = offerId,
            Status = TradeOfferStatus.SENT,
            RetryCount = (data.Attempts ?? 1) - 1,
            SentAt = DateTime.UtcNow,
        };
        _db.Set<TradeOffer>().Add(tradeOffer);

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "trade_offer.sent: persisted offer {OfferId} for transaction {TransactionId} ({Direction}). correlationId={CorrelationId}",
            offerId, transactionId, direction, correlationId);
        return TradeWebhookResult.Applied;
    }

    private async Task<TradeWebhookResult> HandleFailedAsync(
        TradeOfferEventData data, string correlationId, CancellationToken cancellationToken)
    {
        // trade_offer.failed: sidecar exhausted retries (or permanent eResult) —
        // no offer ever reached Steam. We record the attempt on a TradeOffer
        // row (Status=FAILED, SteamTradeOfferId=null) for audit; full transaction
        // cancellation orchestration (refund, reputation, outbox) ships in T69
        // (failover) — see K-list.
        var transactionId = RequireTransactionId(data, SteamWebhookEvents.TradeOfferFailed);
        var direction = ParseDirection(data, SteamWebhookEvents.TradeOfferFailed);
        var botAccountName = data.BotAccountName;

        var transaction = await _db.Set<Transaction>().FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);
        if (transaction is null)
        {
            _logger.LogWarning(
                "trade_offer.failed: transactionId {TransactionId} not found. correlationId={CorrelationId}",
                transactionId, correlationId);
            return TradeWebhookResult.Unknown;
        }

        Guid? botId = null;
        if (!string.IsNullOrWhiteSpace(botAccountName))
        {
            var bot = await _db.Set<PlatformSteamBot>().FirstOrDefaultAsync(b => b.DisplayName == botAccountName, cancellationToken);
            botId = bot?.Id;
        }

        var tradeOffer = new TradeOffer
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            PlatformSteamBotId = botId ?? Guid.Empty,
            Direction = direction,
            SteamTradeOfferId = null,
            Status = TradeOfferStatus.FAILED,
            RetryCount = (data.Attempts ?? 0),
            ErrorMessage = Truncate(data.Reason, 500),
        };

        // PlatformSteamBotId is required by the column (non-null) so when the
        // sidecar omits botAccountName (very early bot failure) we cannot
        // persist the row. Log + ack instead.
        if (tradeOffer.PlatformSteamBotId == Guid.Empty)
        {
            _logger.LogWarning(
                "trade_offer.failed: no bot resolvable (account={Account}) — skipping TradeOffer row. transaction={TransactionId} reason={Reason} correlationId={CorrelationId}",
                botAccountName, transactionId, data.Reason, correlationId);
            return TradeWebhookResult.Applied;
        }

        _db.Set<TradeOffer>().Add(tradeOffer);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning(
            "trade_offer.failed: recorded failure for transaction {TransactionId} ({Direction}) reason={Reason} eresult={EResult} correlationId={CorrelationId}",
            transactionId, direction, data.Reason, data.EResult, correlationId);
        return TradeWebhookResult.Applied;
    }

    private Task<TradeWebhookResult> HandleAcceptedAsync(
        TradeOfferEventData data, string correlationId, CancellationToken cancellationToken)
    {
        return ApplyStatusChangeAsync(
            data, correlationId, cancellationToken,
            newStatus: TradeOfferStatus.ACCEPTED,
            forwardTrigger: direction => direction == DirectionEscrow
                ? TransactionTrigger.EscrowItem
                : TransactionTrigger.DeliverItem,
            // ACCEPTED is the forward path — no cancellation reason needed.
            cancelReason: null);
    }

    private Task<TradeWebhookResult> HandleDeclinedAsync(
        TradeOfferEventData data, string correlationId, CancellationToken cancellationToken)
    {
        return ApplyStatusChangeAsync(
            data, correlationId, cancellationToken,
            newStatus: TradeOfferStatus.DECLINED,
            forwardTrigger: direction => direction == DirectionEscrow
                ? TransactionTrigger.SellerDecline
                : TransactionTrigger.BuyerDecline,
            cancelReason: direction => direction == DirectionEscrow
                ? "Seller trade offer declined (sidecar webhook)."
                : "Buyer trade offer declined (sidecar webhook).");
    }

    private Task<TradeWebhookResult> HandleExpiredAsync(
        TradeOfferEventData data, string correlationId, CancellationToken cancellationToken)
    {
        // Steam-side expiration (state 5) — backend treats as a Timeout-class
        // cancellation regardless of direction.
        return ApplyStatusChangeAsync(
            data, correlationId, cancellationToken,
            newStatus: TradeOfferStatus.EXPIRED,
            forwardTrigger: _ => TransactionTrigger.Timeout,
            cancelReason: _ => "Trade offer expired on Steam (sidecar webhook).");
    }

    private Task<TradeWebhookResult> HandleCancellingAsync(
        TradeOfferEventData data, string correlationId, string reasonTag, CancellationToken cancellationToken)
    {
        // countered (4) / invalid_items (8) — 08 §2.4: treat both as
        // cancellations. The TradeOffer row is moved to DECLINED to share
        // the same surface as a manual decline.
        return ApplyStatusChangeAsync(
            data, correlationId, cancellationToken,
            newStatus: TradeOfferStatus.DECLINED,
            forwardTrigger: direction => direction == DirectionEscrow
                ? TransactionTrigger.SellerDecline
                : TransactionTrigger.BuyerDecline,
            cancelReason: _ => $"Trade offer terminated by sidecar event '{reasonTag}'.");
    }

    /// <summary>
    /// Shared status-change pipeline. Looks the TradeOffer up by SteamTradeOfferId,
    /// flips its status, optionally fires the transaction state machine. Replays
    /// (same status) are no-ops. Side-effect orchestration (timeout cancel,
    /// outbox publish, reputation refresh) is forward-deferred — see K-list.
    /// </summary>
    private async Task<TradeWebhookResult> ApplyStatusChangeAsync(
        TradeOfferEventData data,
        string correlationId,
        CancellationToken cancellationToken,
        TradeOfferStatus newStatus,
        Func<string, TransactionTrigger> forwardTrigger,
        Func<string, string>? cancelReason)
    {
        var offerId = RequireOfferId(data, "trade-status-change");

        var tradeOffer = await _db.Set<TradeOffer>().FirstOrDefaultAsync(
            t => t.SteamTradeOfferId == offerId, cancellationToken);
        if (tradeOffer is null)
        {
            _logger.LogWarning(
                "Trade status change: offer {OfferId} not found locally — sidecar may have fired before sent event landed. correlationId={CorrelationId}",
                offerId, correlationId);
            return TradeWebhookResult.Unknown;
        }

        if (tradeOffer.Status == newStatus)
        {
            _logger.LogInformation(
                "Trade status change: offer {OfferId} already at {Status} — idempotent. correlationId={CorrelationId}",
                offerId, newStatus, correlationId);
            return TradeWebhookResult.Idempotent;
        }

        var direction = tradeOffer.Direction == TradeOfferDirection.TO_SELLER
            ? DirectionEscrow
            : DirectionDelivery;

        tradeOffer.Status = newStatus;
        tradeOffer.RespondedAt = DateTime.UtcNow;

        var transaction = await _db.Set<Transaction>().FirstOrDefaultAsync(
            t => t.Id == tradeOffer.TransactionId, cancellationToken);
        if (transaction is null)
        {
            _logger.LogWarning(
                "Trade status change: transaction {TransactionId} missing for offer {OfferId}. correlationId={CorrelationId}",
                tradeOffer.TransactionId, offerId, correlationId);
            await _db.SaveChangesAsync(cancellationToken);
            return TradeWebhookResult.Unknown;
        }

        var trigger = forwardTrigger(direction);
        var machine = new TransactionStateMachine(transaction);

        if (!machine.CanFire(trigger))
        {
            _logger.LogInformation(
                "Trade status change: transaction {TransactionId} state {State} does not permit {Trigger} — idempotent ack. correlationId={CorrelationId}",
                transaction.Id, transaction.Status, trigger, correlationId);
            await _db.SaveChangesAsync(cancellationToken);
            return TradeWebhookResult.Idempotent;
        }

        try
        {
            if (cancelReason is null)
            {
                machine.Fire(trigger);
            }
            else
            {
                machine.Fire(trigger, new CancellationContext(cancelReason(direction)));
            }
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex,
                "Trade status change: state machine rejected {Trigger} for transaction {TransactionId}. correlationId={CorrelationId}",
                trigger, transaction.Id, correlationId);
            // Persist the TradeOffer status flip even when the state machine
            // refuses (likely a race) so the audit trail still reflects what
            // Steam reported.
            await _db.SaveChangesAsync(cancellationToken);
            return TradeWebhookResult.Idempotent;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Trade status change applied: offer {OfferId} → {Status}; transaction {TransactionId} fired {Trigger}. correlationId={CorrelationId}",
            offerId, newStatus, transaction.Id, trigger, correlationId);
        return TradeWebhookResult.Applied;
    }

    private static Guid RequireTransactionId(TradeOfferEventData data, string eventName)
    {
        if (!data.TransactionId.HasValue || data.TransactionId.Value == Guid.Empty)
        {
            throw new BusinessRuleException("WEBHOOK_TRANSACTION_ID_MISSING", $"transactionId is required for {eventName}.");
        }
        return data.TransactionId.Value;
    }

    private static string RequireOfferId(TradeOfferEventData data, string eventName)
    {
        if (string.IsNullOrWhiteSpace(data.OfferId))
        {
            throw new BusinessRuleException("WEBHOOK_OFFER_ID_MISSING", $"offerId is required for {eventName}.");
        }
        return data.OfferId!;
    }

    private static TradeOfferDirection ParseDirection(TradeOfferEventData data, string eventName)
    {
        return data.Direction switch
        {
            DirectionEscrow => TradeOfferDirection.TO_SELLER,
            DirectionDelivery => TradeOfferDirection.TO_BUYER,
            _ => throw new BusinessRuleException("WEBHOOK_DIRECTION_INVALID", $"direction '{data.Direction}' invalid for {eventName}."),
        };
    }

    private static string RequireBotAccountName(TradeOfferEventData data, string eventName)
    {
        if (string.IsNullOrWhiteSpace(data.BotAccountName))
        {
            throw new BusinessRuleException("WEBHOOK_BOT_ACCOUNT_MISSING", $"botAccountName is required for {eventName}.");
        }
        return data.BotAccountName!;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        return value.Length <= max ? value : value[..max];
    }
}
