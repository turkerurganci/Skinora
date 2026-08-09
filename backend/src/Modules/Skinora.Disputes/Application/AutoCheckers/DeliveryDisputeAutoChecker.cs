using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
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
    private const string DeliveredMessage = DisputeAutoCheckMessages.DeliveryDelivered;
    private const string TradeOfferActiveMessage = DisputeAutoCheckMessages.DeliveryOfferActive;
    private const string NotDeliveredMessage = DisputeAutoCheckMessages.DeliveryNotStarted;

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

        // v3.0 — the TradeOffers table is gone: the platform no longer creates
        // or tracks trade offers (02 §2.1). Delivery is now decided from the
        // evidence recorded on the transaction itself (02 §9.2, 06 §2.24).
        //
        // Full re-implementation — a forced, cache-bypassing verification round
        // plus the "seller's asset gone but nothing arrived" escalation branch —
        // lands with the delivery verification service (11 §5, T130). Until
        // then this reads the already-recorded evidence and fails closed:
        // anything short of proven delivery stays open so the buyer can
        // escalate to a human rather than getting a wrong automated answer.
        if (transaction.DeliveryEvidence.IsSufficientForDelivery())
        {
            return Resolved(DeliveredMessage);
        }

        // Item left the seller but never reached the buyer — a wrong item or a
        // third-party send. This must never resolve silently.
        if (transaction.DeliveryEvidence.IsMisdeliverySignature())
        {
            return Unresolved(TradeOfferActiveMessage);
        }

        return Unresolved(NotDeliveredMessage);
    }

    private static AutoCheckResult Resolved(string messageKey) =>
        new(Resolved: true,
            AutoEscalated: false,
            MessageKey: messageKey,
            CanSubmitTxHash: false,
            CanEscalate: false);

    private static AutoCheckResult Unresolved(string messageKey) =>
        new(Resolved: false,
            AutoEscalated: false,
            MessageKey: messageKey,
            CanSubmitTxHash: false,
            CanEscalate: true);
}
