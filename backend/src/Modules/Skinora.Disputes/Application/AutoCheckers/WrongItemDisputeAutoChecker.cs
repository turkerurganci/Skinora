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
///   <item>No delivery yet (no DeliveredBuyerAssetId / inventory probe blank) → unresolved, buyer escalates manually.</item>
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
    private const string MatchMessage =
        "Teslim edilen item, işlemdeki item ile eşleşiyor";
    private const string AutoEscalatedMessage =
        "Teslim edilen item beklenen item ile eşleşmiyor — işleminiz incelemeye alındı";
    private const string NoDeliveryMessage =
        "Teslim verisi bulunamadı; teslim aşamasına gelinmedi";

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

        var snapshot = await _inventory.TryGetItemAsync(
            buyer.SteamId,
            transaction.DeliveredBuyerAssetId,
            cancellationToken);

        if (snapshot is null)
        {
            // Probe failed (sidecar stub or asset rotated post-delivery). We
            // cannot conclude auto-mismatch from missing data — leave OPEN so
            // the buyer can escalate.
            return Unresolved(NoDeliveryMessage);
        }

        if (string.Equals(snapshot.ClassId, transaction.ItemClassId, StringComparison.Ordinal))
        {
            return Resolved(MatchMessage);
        }

        return AutoEscalated(AutoEscalatedMessage);
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

    private static AutoCheckResult AutoEscalated(string message) =>
        new(Resolved: false,
            AutoEscalated: true,
            Message: message,
            CanSubmitTxHash: false,
            CanEscalate: false);
}
