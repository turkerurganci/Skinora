using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Persistence;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Domain.Entities;

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
/// delegates to <see cref="IBotRecoveryMaterialiser"/>, which (1) materialises a
/// <see cref="BotRecoveryItem"/> (PENDING) and (2) auto-applies an EMERGENCY_HOLD
/// so the transaction's timeout stops ticking while the item is stuck — a user
/// must not be penalised for a delay the platform caused. The same materialiser
/// also backs the boundary-race safety net in
/// <c>SteamWebhookHandler.AcceptEscrowAsync</c>, so the two paths cannot drift.
/// All staged rows commit in this consumer's single <c>SaveChangesAsync</c>.
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
/// the materialiser's up-front existence check skip already-materialised
/// transactions, so a bot flipping restricted→…→restricted (or an outbox
/// redelivery) re-runs harmlessly. The outbox dispatcher wraps each message in its
/// own try/catch, so an unexpected failure here only rolls back this consumer's
/// own unit of work — never a sibling message's — and the outbox retries.
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
    private readonly AppDbContext _db;
    private readonly IBotRecoveryMaterialiser _materialiser;
    private readonly ILogger<BotRestrictionRecoveryConsumer> _logger;

    public BotRestrictionRecoveryConsumer(
        AppDbContext db,
        IBotRecoveryMaterialiser materialiser,
        ILogger<BotRestrictionRecoveryConsumer> logger)
    {
        _db = db;
        _materialiser = materialiser;
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

        var created = 0;
        var held = 0;

        foreach (var transaction in stuck)
        {
            var outcome = await _materialiser.TryMaterialiseAsync(
                transaction, botId, notification.DisplayName, notification.Status, cancellationToken);
            if (outcome.Created)
            {
                created++;
            }
            if (outcome.AutoHeld)
            {
                held++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "BotRestrictionRecovery: bot {BotId} ({DisplayName}) restricted ({Status}) — {Created} recovery item(s) materialised, {Held} auto-held. correlationId(event)={EventId}",
            botId, notification.DisplayName, notification.Status, created, held, notification.EventId);
    }
}
