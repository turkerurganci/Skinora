using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Disputes.Application.AutoCheckers;

/// <summary>
/// T130 — default <see cref="IWrongItemDisputeAutoChecker"/>. Implements 03 §6.3
/// by diffing the buyer's inventory against the 06 §3.5
/// <c>BuyerBaselineClassIds</c> fingerprint taken on entry to
/// <c>SELLER_CONFIRMED</c>, and comparing whatever arrived against the
/// transaction's own item class.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the previous implementation could never fire.</b> It resolved
/// <c>Transaction.DeliveredBuyerAssetId</c> and compared that asset's class
/// against <c>Transaction.ItemClassId</c>. But the only writers of that column
/// take their value from <c>DeliveryVerificationResult.CandidateDeliveredAssetId</c>,
/// which is the diff of a <em>class-scoped</em> baseline read of the
/// transaction's own class — so the id it holds is always of that class and the
/// comparison always matched. And a genuinely wrong item never raises the class
/// count at all, so the column stays NULL and the check fell out at the first
/// guard. The mismatch branch was unreachable in both directions; 02 §10.1's
/// "gelen item'ın adı kayda geçirilerek admin'e yükseltilir" had nothing to
/// name. An inventory-wide reference point is what closes that.
/// </para>
/// <para>
/// <b>Why this makes PAYMENT_RECEIVED meaningful.</b> 02 §10.1 admits the
/// wrong-item dispute from <c>PAYMENT_RECEIVED</c> precisely because a wrong
/// item never lifts the expected class count and so never reaches
/// <c>ITEM_DELIVERED</c>. That state is where the case actually lives, and it is
/// the state in which the old checker had least to say.
/// </para>
/// <para>
/// <b>Read-only.</b> Unlike the delivery checker this one advances nothing: a
/// class mismatch is a question for an admin, never a state transition
/// (03 §6.3 Sonuç B).
/// </para>
/// </remarks>
public sealed class WrongItemDisputeAutoChecker : IWrongItemDisputeAutoChecker
{
    private readonly AppDbContext _db;
    private readonly ISteamInventoryReader _inventory;
    private readonly ILogger<WrongItemDisputeAutoChecker> _logger;

    public WrongItemDisputeAutoChecker(
        AppDbContext db,
        ISteamInventoryReader inventory,
        ILogger<WrongItemDisputeAutoChecker> logger)
    {
        _db = db;
        _inventory = inventory;
        _logger = logger;
    }

    public async Task<AutoCheckResult> CheckAsync(
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // ---------- The reference point ----------
        // No fingerprint means the buyer's inventory was unreadable when the
        // baseline was due (06 §3.5 leaves all four columns NULL together). The
        // comparison has nothing to be measured against, and comparing against
        // an empty set would read every item the buyer owns as a fresh arrival.
        var baselineClassIds = DeserializeBaselineClassIds(transaction);
        if (baselineClassIds is null)
        {
            return Unresolved(DisputeAutoCheckMessages.WrongItemInventoryUnreadable);
        }

        var buyerSteamId = await ResolveBuyerSteamIdAsync(transaction, cancellationToken);
        if (buyerSteamId is null)
        {
            return Unresolved(DisputeAutoCheckMessages.WrongItemInventoryUnreadable);
        }

        // Fresh: 02 §10.1 runs the dispute checks against live state, and a
        // cached read can still be missing an item that arrived a minute ago.
        var fingerprint = await _inventory.CaptureInventoryFingerprintAsync(
            buyerSteamId, InventoryReadFreshness.Fresh, cancellationToken);

        // 08 §2.3 — an unreadable inventory is not an empty one. Diffing it would
        // manufacture the opposite finding: every baseline class would look gone
        // and nothing would look arrived.
        if (fingerprint.Visibility != InventoryVisibility.Public)
        {
            _logger.LogInformation(
                "Transaction {TransactionId}: wrong-item check could not read the buyer's "
                + "inventory ({Visibility}) — no comparison made (08 §2.3)",
                transaction.Id, fingerprint.Visibility);
            return Unresolved(DisputeAutoCheckMessages.WrongItemInventoryUnreadable);
        }

        // ---------- Sonuç A — the expected item is there ----------
        // Counted, not tested for presence, and against the same
        // (classId, instanceId) pair the baseline counted: one class legitimately
        // appears many times in an inventory, so "the buyer owns this skin" would
        // answer A for a buyer who already owned one before the trade (02 §9.2).
        var expectedNow = fingerprint.Assets
            .Count(a => a.Matches(transaction.ItemClassId, transaction.ItemInstanceId));

        if (transaction.BuyerBaselineClassCount is { } baselineCount && expectedNow > baselineCount)
        {
            return Resolved(DisputeAutoCheckMessages.WrongItemMatch);
        }

        // ---------- Sonuç B — something else arrived ----------
        var known = new HashSet<string>(baselineClassIds, StringComparer.Ordinal);
        var arrived = fingerprint.Assets
            .Where(a => !known.Contains(a.ClassId))
            .ToList();

        if (arrived.Count > 0)
        {
            // 06 §8.4's rule, applied to a name instead of an id: ambiguity
            // resolves to null rather than to a guess. Several classes can arrive
            // in the window between the baseline and the dispute — the buyer
            // trades on their own account too — and naming the wrong one in the
            // admin's evidence field is worse than naming none. The escalation
            // itself is unconditional either way.
            var distinctClasses = arrived
                .Select(a => a.ClassId)
                .Distinct(StringComparer.Ordinal)
                .Count();

            var deliveredItemName = distinctClasses == 1 ? Truncate(arrived[0].Name) : null;

            if (deliveredItemName is null)
            {
                _logger.LogWarning(
                    "Transaction {TransactionId}: {Count} distinct classes arrived since the "
                    + "baseline — escalated without naming one, since naming the wrong item is "
                    + "worse than naming none (02 §10.1)",
                    transaction.Id, distinctClasses);
            }
            else
            {
                _logger.LogWarning(
                    "Transaction {TransactionId}: expected class {Expected} did not arrive but "
                    + "'{DeliveredItemName}' did — auto-escalated to admin (03 §6.3 Sonuç B)",
                    transaction.Id, transaction.ItemClassId, deliveredItemName);
            }

            return AutoEscalated(DisputeAutoCheckMessages.WrongItemMismatch, deliveredItemName);
        }

        // ---------- Sonuç C — nothing new arrived ----------
        // "Bu bir yanlış item değil, teslim edilmeme vakasıdır" (03 §6.3): the
        // buyer is pointed at the delivery flow, and the dispute stays open so
        // they can escalate.
        return Unresolved(DisputeAutoCheckMessages.WrongItemNoDelivery);
    }

    /// <summary>
    /// The 06 §3.5 fingerprint, or <c>null</c> when there is none to diff
    /// against.
    /// </summary>
    /// <remarks>
    /// An unparsable column degrades to "no reference" rather than to an empty
    /// set — the two are opposite findings, and the empty one accuses a seller.
    /// </remarks>
    private IReadOnlyList<string>? DeserializeBaselineClassIds(Transaction transaction)
    {
        if (transaction.BuyerBaselineCapturedAt is null) return null;
        if (string.IsNullOrWhiteSpace(transaction.BuyerBaselineClassIds)) return null;

        try
        {
            return JsonSerializer.Deserialize<List<string>>(transaction.BuyerBaselineClassIds);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Transaction {TransactionId} has an unparsable BuyerBaselineClassIds column — "
                + "the wrong-item comparison has no reference point (06 §3.5)", transaction.Id);
            return null;
        }
    }

    private async Task<string?> ResolveBuyerSteamIdAsync(
        Transaction transaction, CancellationToken cancellationToken)
    {
        if (transaction.BuyerId is not { } buyerId) return null;

        var steamId = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == buyerId)
            .Select(u => u.SteamId)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(steamId) ? null : steamId;
    }

    /// <summary>
    /// Keep the name inside the 200-character column (06 §3.11). A name long
    /// enough to hit this is already anomalous; truncating preserves the
    /// evidence an admin needs while the column stays bounded.
    /// </summary>
    private static string Truncate(string name) =>
        name.Length <= DeliveredItemNameMaxLength
            ? name
            : name[..DeliveredItemNameMaxLength];

    private const int DeliveredItemNameMaxLength = 200;

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

    private static AutoCheckResult AutoEscalated(string messageKey, string? deliveredItemName) =>
        new(Resolved: false,
            AutoEscalated: true,
            MessageKey: messageKey,
            CanSubmitTxHash: false,
            CanEscalate: false)
        {
            DeliveredItemName = deliveredItemName,
        };
}
