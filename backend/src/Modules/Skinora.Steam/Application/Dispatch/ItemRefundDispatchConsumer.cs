using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Persistence;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Steam.Application.Dispatch;

/// <summary>
/// T106a refund leg — MediatR notification handler that consumes
/// <see cref="ItemRefundToSellerRequestedEvent"/> from the outbox (published by
/// the timeout / user-cancel / admin-cancel paths) and asks the sidecar to send
/// a <c>BOT_TO_SELLER_REFUND</c> offer returning the escrowed item to the
/// seller. The bot's escrow slot is released when the seller accepts that offer
/// (<c>trade_offer.accepted</c> → refund branch of the webhook handler).
/// </summary>
/// <remarks>
/// Mirrors <c>PostCancelMonitorStartDispatcher</c>: a transient failure (sidecar
/// unreachable / retryable) re-throws so the outbox marks the message FAILED and
/// retries; a permanent failure or a missing prerequisite is logged and treated
/// as terminal (the outbox marks it PROCESSED). Idempotency is keyed on the
/// existence of a <c>RETURN_TO_SELLER</c> TradeOffer row.
/// </remarks>
public sealed class ItemRefundDispatchConsumer
    : INotificationHandler<ItemRefundToSellerRequestedEvent>
{
    private readonly AppDbContext _db;
    private readonly ITradeOfferDispatchClient _client;
    private readonly ILogger<ItemRefundDispatchConsumer> _logger;

    public ItemRefundDispatchConsumer(
        AppDbContext db,
        ITradeOfferDispatchClient client,
        ILogger<ItemRefundDispatchConsumer> logger)
    {
        _db = db;
        _client = client;
        _logger = logger;
    }

    public async Task Handle(
        ItemRefundToSellerRequestedEvent notification, CancellationToken cancellationToken)
    {
        var transaction = await _db.Set<Transaction>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == notification.TransactionId, cancellationToken);
        if (transaction is null)
        {
            _logger.LogWarning(
                "ItemRefund: transaction {TransactionId} not found — nothing to return.",
                notification.TransactionId);
            return;
        }

        // The item is on the platform only if it was actually escrowed (bot +
        // asset id set). Pre-escrow cancellations have nothing to return.
        if (transaction.EscrowBotId is not { } botId || botId == Guid.Empty
            || string.IsNullOrWhiteSpace(transaction.EscrowBotAssetId))
        {
            _logger.LogInformation(
                "ItemRefund: transaction {TransactionId} was not escrowed (no bot/asset) — no return offer needed.",
                notification.TransactionId);
            return;
        }

        // Idempotency — a refund offer was already dispatched for this transaction.
        var alreadyDispatched = await _db.Set<TradeOffer>().AnyAsync(
            o => o.TransactionId == transaction.Id
                && o.Direction == TradeOfferDirection.RETURN_TO_SELLER,
            cancellationToken);
        if (alreadyDispatched)
        {
            _logger.LogInformation(
                "ItemRefund: RETURN_TO_SELLER already dispatched for transaction {TransactionId} — idempotent.",
                transaction.Id);
            return;
        }

        var sellerSteamId = await _db.Set<User>()
            .Where(u => u.Id == notification.SellerId)
            .Select(u => u.SteamId)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(sellerSteamId))
        {
            _logger.LogError(
                "ItemRefund: seller {SellerId} has no SteamId — cannot dispatch refund for transaction {TransactionId}.",
                notification.SellerId, transaction.Id);
            return;
        }

        var botAccountName = await _db.Set<PlatformSteamBot>()
            .Where(b => b.Id == botId)
            .Select(b => b.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(botAccountName))
        {
            _logger.LogError(
                "ItemRefund: escrow bot {BotId} not found — cannot dispatch refund for transaction {TransactionId}.",
                botId, transaction.Id);
            return;
        }

        var request = new TradeOfferDispatchRequest(
            TransactionId: transaction.Id,
            Direction: TradeOfferDispatchDirection.BotToSellerRefund,
            PartnerSteamId: sellerSteamId,
            Items: [new TradeOfferDispatchItem(transaction.EscrowBotAssetId!, SteamConstants.Cs2AppId, SteamConstants.Cs2ContextId)],
            BotAccountName: botAccountName);

        var result = await _client.SendAsync(request, cancellationToken);
        switch (result.Status)
        {
            case TradeOfferDispatchStatus.Sent:
            case TradeOfferDispatchStatus.Pending:
                _logger.LogInformation(
                    "ItemRefund: RETURN_TO_SELLER dispatched for transaction {TransactionId} via bot {BotId} (offer {OfferId}, trigger {Trigger}).",
                    transaction.Id, botId, result.OfferId, notification.Trigger);
                return;

            case TradeOfferDispatchStatus.Failed when !result.Retryable:
                // Permanent — re-sending the same payload will keep failing.
                // Leave for manual recovery; do not block the outbox chain.
                _logger.LogError(
                    "ItemRefund: RETURN_TO_SELLER permanently failed for transaction {TransactionId}: {Reason}. Manual recovery required.",
                    transaction.Id, result.Reason);
                return;

            default:
                // Unavailable or retryable Failed — let the outbox retry the event.
                throw new InvalidOperationException(
                    $"Refund dispatch unavailable (status={result.Status}, reason={result.Reason}) for transaction {transaction.Id}; outbox will retry.");
        }
    }
}
