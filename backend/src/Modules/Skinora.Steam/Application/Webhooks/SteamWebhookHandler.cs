using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Application.Audit;
using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Steam.Application.Webhooks;

/// <summary>
/// T68 webhook dispatcher (T69 extended bot lifecycle handling). Each
/// invocation runs in a single <see cref="AppDbContext"/> transaction so the
/// TradeOffer upsert and the state-machine flip share the same SaveChanges —
/// a partial commit cannot leave the system with an offer row but no state
/// advance.
/// </summary>
public sealed class SteamWebhookHandler : ISteamWebhookHandler
{
    // Internal direction strings used by the shared status-change pipeline to
    // pick the forward trigger (computed from the persisted TradeOffer.Direction,
    // NOT from the wire payload).
    private const string DirectionEscrow = "escrow";
    private const string DirectionDelivery = "delivery";

    // T106a — the sidecar's on-the-wire `direction` vocabulary (matches its
    // TradeDirection union, sidecar-steam/src/trade/types.ts). Carried on
    // trade_offer.sent / trade_offer.failed; reconciled to TradeOfferDirection
    // by ParseDirection. (Status-change events carry no direction — the backend
    // resolves it from the stored TradeOffer row.)
    private const string SidecarDirectionEscrow = "SELLER_TO_BOT";
    private const string SidecarDirectionDelivery = "BOT_TO_BUYER";
    private const string SidecarDirectionRefund = "BOT_TO_SELLER_REFUND";

    private readonly AppDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly INotificationRealtimePublisher _realtime;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;
    private readonly ILogger<SteamWebhookHandler> _logger;

    public SteamWebhookHandler(
        AppDbContext db,
        IAuditLogger auditLogger,
        INotificationRealtimePublisher realtime,
        IOutboxService outbox,
        TimeProvider clock,
        ILogger<SteamWebhookHandler> logger)
    {
        _db = db;
        _auditLogger = auditLogger;
        _realtime = realtime;
        _outbox = outbox;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleBotEventAsync(
        SteamWebhookEnvelope<BotEventData> envelope,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // T69 — sidecar reports BotFailureReason via signed webhook; we map
        // it onto PlatformSteamBotStatus and persist the transition with an
        // audit row, then push the change to admin SignalR clients. Bot
        // events keep their structured Warning log even after mutation —
        // operators tailing Loki still see the full lifecycle.
        var data = envelope.Data;
        _logger.LogWarning(
            "Steam bot event received: {Event} account={Account} reason={Reason} status={Status} correlationId={CorrelationId}",
            envelope.Event,
            data?.AccountName,
            data?.Reason,
            data?.Status,
            correlationId);

        if (data is null || string.IsNullOrWhiteSpace(data.AccountName))
        {
            _logger.LogWarning(
                "Bot event payload missing accountName — skipping status update. correlationId={CorrelationId}",
                correlationId);
            return;
        }

        var bot = await _db.Set<PlatformSteamBot>()
            .FirstOrDefaultAsync(b => b.DisplayName == data.AccountName, cancellationToken);
        if (bot is null)
        {
            _logger.LogWarning(
                "Bot event references unknown account {Account} — no PlatformSteamBot row, skipping update. correlationId={CorrelationId}",
                data.AccountName, correlationId);
            return;
        }

        var targetStatus = MapReasonToStatus(data.Reason);
        var previousStatus = bot.Status;
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        // Idempotent — same status arrives a second time (sidecar retry,
        // duplicate health-probe failure). Still refresh LastHealthCheckAt
        // because the probe observation is fresh, but skip audit + push so
        // we do not flood the admin dashboard with duplicate banners.
        if (previousStatus == targetStatus)
        {
            bot.LastHealthCheckAt = nowUtc;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Bot {Account} already at status {Status} — idempotent ack. correlationId={CorrelationId}",
                data.AccountName, targetStatus, correlationId);
            return;
        }

        bot.Status = targetStatus;
        bot.LastHealthCheckAt = nowUtc;
        // T103b-2 — surface the sidecar reason for non-ACTIVE statuses (07 §9.10
        // restrictionReason); cleared if a bot ever returns to ACTIVE.
        bot.RestrictionReason = targetStatus == PlatformSteamBotStatus.ACTIVE
            ? null
            : Truncate(data.Reason, 200);

        await _auditLogger.LogAsync(new AuditLogEntry(
            UserId: null,
            ActorId: SeedConstants.SystemUserId,
            ActorType: ActorType.SYSTEM,
            Action: AuditAction.BOT_STATUS_CHANGED,
            EntityType: nameof(PlatformSteamBot),
            EntityId: bot.Id.ToString(),
            OldValue: previousStatus.ToString(),
            NewValue: $"{targetStatus};reason={data.Reason};event={envelope.Event}",
            IpAddress: null), cancellationToken);

        // T103b-2 — a durable restriction (RESTRICTED/BANNED) puts every item
        // still in this bot's custody at risk. Raise BotRestrictedEvent so the
        // recovery consumer materialises the queue + auto-holds those
        // transactions. Published in the same unit of work as the status flip
        // (outbox row commits atomically with the SaveChanges below). OFFLINE is
        // treated as transient (see BotRestrictedEvent remarks) and excluded.
        if (targetStatus is PlatformSteamBotStatus.RESTRICTED or PlatformSteamBotStatus.BANNED)
        {
            await _outbox.PublishAsync(
                new BotRestrictedEvent(
                    EventId: Guid.NewGuid(),
                    PlatformSteamBotId: bot.Id,
                    SteamId: bot.SteamId,
                    DisplayName: bot.DisplayName,
                    Status: targetStatus.ToString(),
                    Reason: data.Reason,
                    OccurredAt: nowUtc),
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _realtime.PublishAdminBotStatusChangedAsync(
            new NotificationRealtimePayloads.AdminBotStatusChanged(
                BotId: bot.Id,
                SteamId: bot.SteamId,
                DisplayName: bot.DisplayName,
                PreviousStatus: previousStatus.ToString(),
                NewStatus: targetStatus.ToString(),
                Reason: data.Reason ?? string.Empty,
                ChangedAt: nowUtc),
            cancellationToken);
    }

    /// <summary>
    /// Translate the sidecar <c>BotFailureReason</c> (TypeScript union, see
    /// <c>sidecar-steam/src/bot/BotSession.ts</c>) into the persisted
    /// <see cref="PlatformSteamBotStatus"/>. Unknown reasons collapse to
    /// <see cref="PlatformSteamBotStatus.OFFLINE"/> rather than ACTIVE so we
    /// never silently re-eligibilise a bot the sidecar took out of the pool.
    /// </summary>
    private static PlatformSteamBotStatus MapReasonToStatus(string? reason) => reason switch
    {
        "banned" => PlatformSteamBotStatus.BANNED,
        "restricted" => PlatformSteamBotStatus.RESTRICTED,
        "rate_limited" => PlatformSteamBotStatus.RESTRICTED,
        "login_failed" => PlatformSteamBotStatus.OFFLINE,
        "session_recovery_failed" => PlatformSteamBotStatus.OFFLINE,
        _ => PlatformSteamBotStatus.OFFLINE,
    };

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

        // T106a — the dispatch job records a FAILED row synchronously on a
        // terminal sidecar response; this async webhook reports the same
        // failure. Dedupe so a single dispatch failure does not stack two
        // FAILED rows for one leg (the candidate scan keys off row existence).
        var alreadyRecorded = await _db.Set<TradeOffer>().AnyAsync(
            t => t.TransactionId == transactionId
                && t.Direction == direction
                && t.Status == TradeOfferStatus.FAILED,
            cancellationToken);
        if (alreadyRecorded)
        {
            _logger.LogInformation(
                "trade_offer.failed: a FAILED {Direction} offer already recorded for transaction {TransactionId} — idempotent. correlationId={CorrelationId}",
                direction, transactionId, correlationId);
            return TradeWebhookResult.Idempotent;
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

    /// <summary>
    /// T106a — trade_offer.accepted. Unlike the generic status-change pipeline
    /// this path must (1) capture the post-settlement asset id into the
    /// transaction before firing the guard-protected forward trigger and
    /// (2) maintain the bot's denormalized <c>ActiveEscrowCount</c>. Handles all
    /// three directions; the refund leg has no forward trigger (the transaction
    /// is already terminal) — it only releases the bot's escrow slot.
    /// </summary>
    private async Task<TradeWebhookResult> HandleAcceptedAsync(
        TradeOfferEventData data, string correlationId, CancellationToken cancellationToken)
    {
        var offerId = RequireOfferId(data, SteamWebhookEvents.TradeOfferAccepted);

        var tradeOffer = await _db.Set<TradeOffer>().FirstOrDefaultAsync(
            t => t.SteamTradeOfferId == offerId, cancellationToken);
        if (tradeOffer is null)
        {
            _logger.LogWarning(
                "trade_offer.accepted: offer {OfferId} not found locally — sidecar may have fired before sent landed. correlationId={CorrelationId}",
                offerId, correlationId);
            return TradeWebhookResult.Unknown;
        }

        if (tradeOffer.Status == TradeOfferStatus.ACCEPTED)
        {
            _logger.LogInformation(
                "trade_offer.accepted: offer {OfferId} already ACCEPTED — idempotent. correlationId={CorrelationId}",
                offerId, correlationId);
            return TradeWebhookResult.Idempotent;
        }

        var transaction = await _db.Set<Transaction>().FirstOrDefaultAsync(
            t => t.Id == tradeOffer.TransactionId, cancellationToken);
        if (transaction is null)
        {
            _logger.LogWarning(
                "trade_offer.accepted: transaction {TransactionId} missing for offer {OfferId}. correlationId={CorrelationId}",
                tradeOffer.TransactionId, offerId, correlationId);
            tradeOffer.Status = TradeOfferStatus.ACCEPTED;
            tradeOffer.RespondedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return TradeWebhookResult.Unknown;
        }

        return tradeOffer.Direction switch
        {
            TradeOfferDirection.TO_SELLER => await AcceptEscrowAsync(
                tradeOffer, transaction, data, offerId, correlationId, cancellationToken),
            TradeOfferDirection.TO_BUYER => await AcceptDeliveryAsync(
                tradeOffer, transaction, data, offerId, correlationId, cancellationToken),
            TradeOfferDirection.RETURN_TO_SELLER => await AcceptRefundAsync(
                tradeOffer, offerId, correlationId, cancellationToken),
            _ => TradeWebhookResult.Idempotent,
        };
    }

    private async Task<TradeWebhookResult> AcceptEscrowAsync(
        TradeOffer tradeOffer, Transaction transaction, TradeOfferEventData data,
        string offerId, string correlationId, CancellationToken cancellationToken)
    {
        // 06 §3.5 — the ITEM_ESCROWED guard requires EscrowBotAssetId. Capture
        // the bot's new asset id from the sidecar before firing.
        if (!string.IsNullOrWhiteSpace(data.ReceivedAssetId))
        {
            transaction.EscrowBotAssetId = data.ReceivedAssetId;
        }

        var machine = new TransactionStateMachine(transaction);
        if (!machine.CanFire(TransactionTrigger.EscrowItem))
        {
            // Either a replay (already advanced) or EscrowBotAssetId is still
            // missing because the sidecar exchange-details fetch failed. Persist
            // the offer flip and ack — never silently advance without the asset
            // lineage. A missing asset id is logged loudly for ops follow-up.
            tradeOffer.Status = TradeOfferStatus.ACCEPTED;
            tradeOffer.RespondedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(transaction.EscrowBotAssetId))
            {
                _logger.LogError(
                    "trade_offer.accepted (escrow): EscrowBotAssetId missing for transaction {TransactionId} (offer {OfferId}) — cannot advance to ITEM_ESCROWED. correlationId={CorrelationId}",
                    transaction.Id, offerId, correlationId);
            }
            return TradeWebhookResult.Idempotent;
        }

        tradeOffer.Status = TradeOfferStatus.ACCEPTED;
        tradeOffer.RespondedAt = DateTime.UtcNow;
        await AdjustEscrowCountAsync(tradeOffer.PlatformSteamBotId, +1, cancellationToken);
        machine.Fire(TransactionTrigger.EscrowItem);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "trade_offer.accepted (escrow): transaction {TransactionId} → ITEM_ESCROWED, bot {BotId} escrow count +1. correlationId={CorrelationId}",
            transaction.Id, tradeOffer.PlatformSteamBotId, correlationId);
        return TradeWebhookResult.Applied;
    }

    private async Task<TradeWebhookResult> AcceptDeliveryAsync(
        TradeOffer tradeOffer, Transaction transaction, TradeOfferEventData data,
        string offerId, string correlationId, CancellationToken cancellationToken)
    {
        // 06 §3.5 — the ITEM_DELIVERED guard requires DeliveredBuyerAssetId.
        if (!string.IsNullOrWhiteSpace(data.DeliveredAssetId))
        {
            transaction.DeliveredBuyerAssetId = data.DeliveredAssetId;
        }

        var machine = new TransactionStateMachine(transaction);
        if (!machine.CanFire(TransactionTrigger.DeliverItem))
        {
            tradeOffer.Status = TradeOfferStatus.ACCEPTED;
            tradeOffer.RespondedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(transaction.DeliveredBuyerAssetId))
            {
                _logger.LogError(
                    "trade_offer.accepted (delivery): DeliveredBuyerAssetId missing for transaction {TransactionId} (offer {OfferId}) — cannot advance to ITEM_DELIVERED. correlationId={CorrelationId}",
                    transaction.Id, offerId, correlationId);
            }
            return TradeWebhookResult.Idempotent;
        }

        tradeOffer.Status = TradeOfferStatus.ACCEPTED;
        tradeOffer.RespondedAt = DateTime.UtcNow;
        // Item left the bot — release its escrow slot.
        await AdjustEscrowCountAsync(tradeOffer.PlatformSteamBotId, -1, cancellationToken);
        machine.Fire(TransactionTrigger.DeliverItem);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "trade_offer.accepted (delivery): transaction {TransactionId} → ITEM_DELIVERED, bot {BotId} escrow count -1. correlationId={CorrelationId}",
            transaction.Id, tradeOffer.PlatformSteamBotId, correlationId);
        return TradeWebhookResult.Applied;
    }

    private async Task<TradeWebhookResult> AcceptRefundAsync(
        TradeOffer tradeOffer, string offerId, string correlationId, CancellationToken cancellationToken)
    {
        // Refund (RETURN_TO_SELLER) accepted by the seller — the transaction is
        // already terminal (CANCELLED_*), so there is no forward trigger. The
        // item left the bot back to the seller: release its escrow slot.
        tradeOffer.Status = TradeOfferStatus.ACCEPTED;
        tradeOffer.RespondedAt = DateTime.UtcNow;
        await AdjustEscrowCountAsync(tradeOffer.PlatformSteamBotId, -1, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "trade_offer.accepted (refund): offer {OfferId} returned item to seller, bot {BotId} escrow count -1. correlationId={CorrelationId}",
            offerId, tradeOffer.PlatformSteamBotId, correlationId);
        return TradeWebhookResult.Applied;
    }

    /// <summary>
    /// Adjust a bot's denormalized <c>ActiveEscrowCount</c> (06 §3.10) within the
    /// caller's unit of work. The backend is the single writer; the count never
    /// goes negative. No-op when the bot id is empty or the row is gone.
    /// </summary>
    private async Task AdjustEscrowCountAsync(Guid botId, int delta, CancellationToken cancellationToken)
    {
        if (botId == Guid.Empty) return;
        var bot = await _db.Set<PlatformSteamBot>().FirstOrDefaultAsync(b => b.Id == botId, cancellationToken);
        if (bot is null)
        {
            _logger.LogWarning(
                "AdjustEscrowCountAsync: bot {BotId} not found — escrow count {Delta} skipped.", botId, delta);
            return;
        }
        var next = bot.ActiveEscrowCount + delta;
        bot.ActiveEscrowCount = next < 0 ? 0 : next;
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
            SidecarDirectionEscrow => TradeOfferDirection.TO_SELLER,
            SidecarDirectionDelivery => TradeOfferDirection.TO_BUYER,
            SidecarDirectionRefund => TradeOfferDirection.RETURN_TO_SELLER,
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
