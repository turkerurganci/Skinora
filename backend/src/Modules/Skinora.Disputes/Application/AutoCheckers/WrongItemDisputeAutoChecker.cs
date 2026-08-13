using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Disputes.Application.AutoCheckers;

/// <summary>
/// Default <see cref="IWrongItemDisputeAutoChecker"/>. Compares the
/// transaction's <c>ItemClassId</c> snapshot (set at creation time, 06 §3.5)
/// against the actually delivered asset's class id resolved through
/// <see cref="ISteamInventoryReader"/>. Implements 02 §10.1 third row +
/// 03 §6.3:
/// <list type="bullet">
///   <item>Delivered class matches → "Teslim edilen item, işlemdeki item ile eşleşiyor" (Sonuç A — closed).</item>
///   <item>Delivered class does NOT match → AUTO-ESCALATED, both parties notified (Sonuç B).</item>
///   <item>No delivery yet (no DeliveredBuyerAssetId, asset absent from the buyer's inventory, or that inventory unreadable) → unresolved, buyer escalates manually.</item>
/// </list>
/// </summary>
/// <remarks>
/// The auto-escalate branch is the only place in T58 that promotes a dispute
/// directly to ESCALATED on open — by design, because a class-id mismatch is
/// a strong system-side anomaly signal (admin must inspect). All other paths
/// either close the dispute on the spot or leave it OPEN.
/// </remarks>
public sealed class WrongItemDisputeAutoChecker : IWrongItemDisputeAutoChecker
{
    private const string MatchMessage = DisputeAutoCheckMessages.WrongItemMatch;
    private const string AutoEscalatedMessage = DisputeAutoCheckMessages.WrongItemMismatch;
    private const string NoDeliveryMessage = DisputeAutoCheckMessages.WrongItemNoDelivery;

    private readonly AppDbContext _db;
    private readonly ISteamInventoryReader _inventory;

    public WrongItemDisputeAutoChecker(AppDbContext db, ISteamInventoryReader inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    public async Task<AutoCheckResult> CheckAsync(
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (string.IsNullOrWhiteSpace(transaction.DeliveredBuyerAssetId))
        {
            return Unresolved(NoDeliveryMessage);
        }

        var buyer = await _db.Set<User>()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == transaction.BuyerId, cancellationToken);

        if (buyer is null || string.IsNullOrWhiteSpace(buyer.SteamId))
        {
            return Unresolved(NoDeliveryMessage);
        }

        // T123 — Cached: this is after-the-fact evidence gathering about an
        // asset that either arrived or did not, minutes-to-days ago. A
        // 120-second-old snapshot cannot flip that answer, and the checker
        // never advances a state on the strength of it.
        var lookup = await _inventory.GetItemAsync(
            buyer.SteamId,
            transaction.DeliveredBuyerAssetId,
            InventoryReadFreshness.Cached,
            cancellationToken);

        // T121 — 08 §2.3: an unreadable inventory is not a blank inventory.
        // This branch still leaves the dispute OPEN (nothing can be concluded
        // either way), but it is now reached deliberately instead of falling
        // out of a shared `snapshot is null`. The buyer-facing wording for the
        // hidden-inventory case is 03 §6.2 Sonuç D and belongs to T130, which
        // rewrites this checker on top of the delivery verification service;
        // inventing a message key here would pre-empt that decision.
        if (lookup.Visibility is InventoryVisibility.Private or InventoryVisibility.Unavailable)
        {
            return Unresolved(NoDeliveryMessage);
        }

        var snapshot = lookup.Item;
        if (snapshot is null)
        {
            // Inventory read, asset absent — it rotated post-delivery or never
            // arrived. Still not an auto-mismatch: leave OPEN so the buyer can
            // escalate.
            return Unresolved(NoDeliveryMessage);
        }

        if (string.Equals(snapshot.ClassId, transaction.ItemClassId, StringComparison.Ordinal))
        {
            return Resolved(MatchMessage);
        }

        return AutoEscalated(AutoEscalatedMessage);
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

    private static AutoCheckResult AutoEscalated(string messageKey) =>
        new(Resolved: false,
            AutoEscalated: true,
            MessageKey: messageKey,
            CanSubmitTxHash: false,
            CanEscalate: false);
}
