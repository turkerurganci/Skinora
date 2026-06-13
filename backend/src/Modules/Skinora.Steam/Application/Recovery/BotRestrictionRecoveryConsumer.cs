using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Steam.Application.Recovery;

/// <summary>
/// T103b-2 — MediatR notification handler that consumes
/// <see cref="BotRestrictedEvent"/> (published by the Steam webhook handler when
/// a bot transitions into RESTRICTED/BANNED) and opens the S18 Recovery Queue for
/// that bot (02 §15, 03 §11.2a, 04 §8.7).
/// </summary>
/// <remarks>
/// <para>
/// For every transaction whose item is still physically in the bot's custody it
/// (1) materialises a <see cref="BotRecoveryItem"/> (PENDING) and (2) auto-applies
/// an EMERGENCY_HOLD so the transaction's timeout stops ticking while the item is
/// stuck — a user must not be penalised for a delay the platform caused. The hold
/// path mirrors the T59 emergency-hold orchestrator (freeze pre-pass → state
/// machine → outbox notification → audit), composed into one
/// <c>SaveChangesAsync</c> with the recovery rows.
/// </para>
/// <para>
/// <b>Item-in-custody predicate.</b> The escrow leg was accepted
/// (<c>EscrowBotAssetId</c> set) and the item has not left the bot — neither
/// delivered (<c>DeliveredBuyerAssetId</c> null) nor returned to the seller (no
/// accepted RETURN_TO_SELLER offer). This is exactly the set the bot's
/// <c>ActiveEscrowCount</c> tracks.
/// </para>
/// <para>
/// <b>Idempotency.</b> A unique index on <c>BotRecoveryItem.TransactionId</c> plus
/// an up-front existence check skip already-materialised transactions, so a bot
/// flipping restricted→…→restricted (or an outbox redelivery) re-runs harmlessly.
/// On an unexpected failure the single <c>SaveChangesAsync</c> rolls back the whole
/// batch and the outbox retries — no partial state.
/// </para>
/// <para>
/// <b>Terminal transactions.</b> A cancelled transaction whose refund offer has
/// not completed still has its item in the bot — it is added to the queue but not
/// held (nothing to freeze); manual recovery returns the item via another path.
/// </para>
/// </remarks>
public sealed class BotRestrictionRecoveryConsumer
    : INotificationHandler<BotRestrictedEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly AppDbContext _db;
    private readonly ITimeoutFreezeService _freeze;
    private readonly IOutboxService _outbox;
    private readonly IAuditLogger _audit;
    private readonly TimeProvider _clock;
    private readonly ILogger<BotRestrictionRecoveryConsumer> _logger;

    public BotRestrictionRecoveryConsumer(
        AppDbContext db,
        ITimeoutFreezeService freeze,
        IOutboxService outbox,
        IAuditLogger audit,
        TimeProvider clock,
        ILogger<BotRestrictionRecoveryConsumer> logger)
    {
        _db = db;
        _freeze = freeze;
        _outbox = outbox;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task Handle(BotRestrictedEvent notification, CancellationToken cancellationToken)
    {
        var botId = notification.PlatformSteamBotId;

        // Items still in this bot's custody: escrow accepted (asset id captured),
        // not delivered, not returned to seller.
        var stuck = await _db.Set<Transaction>()
            .Where(t => t.EscrowBotId == botId
                && t.EscrowBotAssetId != null
                && t.DeliveredBuyerAssetId == null
                && !t.IsDeleted
                && !_db.Set<TradeOffer>().Any(o => o.TransactionId == t.Id
                    && o.Direction == TradeOfferDirection.RETURN_TO_SELLER
                    && o.Status == TradeOfferStatus.ACCEPTED))
            .ToListAsync(cancellationToken);

        if (stuck.Count == 0)
        {
            _logger.LogInformation(
                "BotRestrictionRecovery: bot {BotId} ({DisplayName}) restricted ({Status}) but holds no in-custody items — nothing to recover.",
                botId, notification.DisplayName, notification.Status);
            return;
        }

        // Idempotency — skip transactions that already have a recovery row.
        var stuckIds = stuck.Select(t => t.Id).ToList();
        var alreadyMaterialised = (await _db.Set<BotRecoveryItem>()
            .Where(r => stuckIds.Contains(r.TransactionId))
            .Select(r => r.TransactionId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var occurredAt = _clock.GetUtcNow().UtcDateTime;
        var holdReason =
            $"Bot {notification.DisplayName} kısıtlandı ({notification.Status}) — emanetteki item recovery bekliyor.";

        var created = 0;
        var held = 0;

        foreach (var transaction in stuck)
        {
            if (alreadyMaterialised.Contains(transaction.Id))
            {
                continue;
            }

            var item = new BotRecoveryItem
            {
                Id = Guid.NewGuid(),
                PlatformSteamBotId = botId,
                TransactionId = transaction.Id,
                RecoveryStatus = BotRecoveryStatus.PENDING,
                StatusAtRestriction = transaction.Status,
            };
            _db.Set<BotRecoveryItem>().Add(item);
            created++;

            // Auto-hold the transaction so its timeout stops while the item is
            // stuck. Skip when already held (e.g. fraud/sanctions hold) or
            // terminal (cancelled awaiting refund — nothing to freeze). The
            // !IsOnHold + non-empty reason guards mean ApplyEmergencyHold cannot
            // throw, so the freeze + hold compose cleanly into the batch commit.
            var autoHeld = false;
            if (!transaction.IsOnHold && !IsTerminalState(transaction.Status))
            {
                await _freeze.FreezeAsync(transaction, TimeoutFreezeReason.EMERGENCY_HOLD, cancellationToken);

                var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
                machine.ApplyEmergencyHold(SeedConstants.SystemUserId, holdReason);
                autoHeld = true;
                held++;

                await _outbox.PublishAsync(
                    new EmergencyHoldAppliedEvent(
                        EventId: Guid.NewGuid(),
                        TransactionId: transaction.Id,
                        SellerId: transaction.SellerId,
                        BuyerId: transaction.BuyerId,
                        ItemName: transaction.ItemName,
                        Reason: holdReason,
                        OccurredAt: occurredAt),
                    cancellationToken);

                await _audit.LogAsync(
                    new AuditLogEntry(
                        UserId: null,
                        ActorId: SeedConstants.SystemUserId,
                        ActorType: ActorType.SYSTEM,
                        Action: AuditAction.EMERGENCY_HOLD_APPLIED,
                        EntityType: nameof(Transaction),
                        EntityId: transaction.Id.ToString(),
                        OldValue: null,
                        NewValue: JsonSerializer.Serialize(new
                        {
                            Reason = holdReason,
                            PreviousStatus = transaction.Status.ToString(),
                            BotRestrictionHold = true,
                        }, JsonOptions),
                        IpAddress: null),
                    cancellationToken);
            }

            await _audit.LogAsync(
                new AuditLogEntry(
                    UserId: null,
                    ActorId: SeedConstants.SystemUserId,
                    ActorType: ActorType.SYSTEM,
                    Action: AuditAction.BOT_RECOVERY_ITEM_CREATED,
                    EntityType: nameof(BotRecoveryItem),
                    EntityId: item.Id.ToString(),
                    OldValue: null,
                    NewValue: JsonSerializer.Serialize(new
                    {
                        BotId = botId,
                        TransactionId = transaction.Id,
                        StatusAtRestriction = transaction.Status.ToString(),
                        AutoHeld = autoHeld,
                    }, JsonOptions),
                    IpAddress: null),
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "BotRestrictionRecovery: bot {BotId} ({DisplayName}) restricted ({Status}) — {Created} recovery item(s) materialised, {Held} auto-held. correlationId(event)={EventId}",
            botId, notification.DisplayName, notification.Status, created, held, notification.EventId);
    }

    private static bool IsTerminalState(TransactionStatus status) => status switch
    {
        TransactionStatus.COMPLETED => true,
        TransactionStatus.CANCELLED_TIMEOUT => true,
        TransactionStatus.CANCELLED_SELLER => true,
        TransactionStatus.CANCELLED_BUYER => true,
        TransactionStatus.CANCELLED_ADMIN => true,
        _ => false,
    };
}
