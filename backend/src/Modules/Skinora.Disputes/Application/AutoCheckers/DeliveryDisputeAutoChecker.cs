using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Steam.Domain.Entities;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Disputes.Application.AutoCheckers;

/// <summary>
/// Default <see cref="IDeliveryDisputeAutoChecker"/> backed by the
/// <c>TradeOffers</c> table (06 §3.9) and the
/// <see cref="ISteamInventoryReader"/> port. Implements the 02 §10.1 second
/// row + 03 §6.2 flow:
/// <list type="bullet">
///   <item>
///     If a TO_BUYER trade offer is ACCEPTED → "Item envanterinize teslim
///     edilmiş durumda" (Sonuç B). The dispute closes immediately.
///   </item>
///   <item>
///     If the trade offer is still PENDING / SENT and the inventory probe
///     finds the asset on the buyer's account → same Sonuç B.
///   </item>
///   <item>
///     Otherwise → "Trade offer'ınız aktif, lütfen Steam üzerinden kabul
///     edin" (Sonuç A). The dispute stays OPEN; the buyer may escalate.
///   </item>
/// </list>
/// </summary>
/// <remarks>
/// The inventory probe is the same forward-deferred port used during
/// transaction creation (T67 swaps the sidecar implementation). Until the
/// sidecar lands, <see cref="StubSteamInventoryReader"/> returns <c>null</c>
/// for every asset id — that fails closed: the inventory branch never
/// resolves so the dispute stays OPEN and the buyer must escalate manually.
/// Trade-offer-status branch (ACCEPTED) keeps working because it only reads
/// the local DB.
/// </remarks>
public sealed class DeliveryDisputeAutoChecker : IDeliveryDisputeAutoChecker
{
    private const string DeliveredMessage = "Item envanterinize teslim edilmiş durumda";
    private const string TradeOfferActiveMessage =
        "Trade offer'ınız aktif, lütfen Steam üzerinden kabul edin";
    private const string NotDeliveredMessage =
        "Trade offer henüz oluşturulmadı; teslim aşamasına gelinmedi";

    private readonly AppDbContext _db;
    private readonly ISteamInventoryReader _inventory;

    public DeliveryDisputeAutoChecker(AppDbContext db, ISteamInventoryReader inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    public async Task<AutoCheckResult> CheckAsync(
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var latestOffer = await _db.Set<TradeOffer>()
            .Where(o => o.TransactionId == transaction.Id
                     && o.Direction == TradeOfferDirection.TO_BUYER)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestOffer is null)
        {
            return Unresolved(NotDeliveredMessage);
        }

        // Local-only happy path — no Steam round-trip needed.
        if (latestOffer.Status == TradeOfferStatus.ACCEPTED)
        {
            return Resolved(DeliveredMessage);
        }

        // Inventory probe (TO_BUYER pending / sent). Stub returns null until
        // T67 sidecar lands — fails closed (stays OPEN), buyer escalates.
        var buyer = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == transaction.BuyerId, cancellationToken);

        if (buyer is null || string.IsNullOrWhiteSpace(buyer.SteamId))
        {
            return Unresolved(TradeOfferActiveMessage);
        }

        // Probe by the asset id the buyer is expected to land — prefer the
        // recorded delivered asset id if the sidecar has populated it; fall
        // back to the original ItemAssetId snapshot for ITEM_DELIVERED-like
        // states where the trade was completed but the snapshot column is
        // unset (defensive — keeps the stub-injected test path stable too).
        var probeAssetId = transaction.DeliveredBuyerAssetId ?? transaction.ItemAssetId;

        var snapshot = await _inventory.TryGetItemAsync(
            buyer.SteamId,
            probeAssetId,
            cancellationToken);

        return snapshot is not null
            ? Resolved(DeliveredMessage)
            : Unresolved(TradeOfferActiveMessage);
    }

    private static AutoCheckResult Resolved(string message) =>
        new(Resolved: true,
            AutoEscalated: false,
            Message: message,
            CanSubmitTxHash: false,
            CanEscalate: false);

    private static AutoCheckResult Unresolved(string message) =>
        new(Resolved: false,
            AutoEscalated: false,
            Message: message,
            CanSubmitTxHash: false,
            CanEscalate: true);
}
