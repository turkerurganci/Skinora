using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Settlement;

/// <summary>
/// T129 — default <see cref="ISettlementVerificationService"/>. Answers 03 §2.4
/// step 2 ("ödeme yapılmadan hemen önce: item hâlâ alıcının envanterinde mi?")
/// with the two reads the platform is allowed to make.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the seller's side is read at all.</b> 02 §4.5.1 words the check as a
/// single question about the buyer, and treats a missing item as proof the
/// trade was reversed. That equivalence does not hold for the whole window:
/// Steam restricts a traded item for 7 days (T122 runbook §6.1) but the default
/// settlement window is 8, so for the last day the buyer can legitimately trade
/// the skin onward. Read one-sidedly, that buyer would be refunded in full
/// <em>and</em> keep the proceeds, while an honest seller lost the item, the
/// money and gained a fraud flag — the exact mirror of the fraud this mechanism
/// exists to close. A reversal returns the item to the SELLER; an onward sale
/// does not. So the seller's side is what separates them, and it is read only
/// when the buyer's side says the item is gone (owner decision, 2026-08-16).
/// </para>
/// <para>
/// <b>Why the buyer test prefers the asset id.</b> Asset ids rotate on every
/// trade (06 §8.4), which makes them worthless for detecting an ARRIVAL — but
/// the id the platform recorded when the item landed is the buyer's own id for
/// it, and it stays stable while the item sits in their inventory. Testing it
/// is exact: it cannot be confused by other copies of the same skin the buyer
/// owns or acquires during the window. The class-count route is the fallback
/// for deliveries confirmed by the buyer without an inventory read, and it is
/// deliberately the weaker of the two.
/// </para>
/// <para>
/// <b>What this service never does.</b> It never converts an unreadable
/// inventory into a finding (08 §2.3) and never returns a verdict that moves
/// money on partial information. Everything it cannot establish comes back as
/// <see cref="SettlementVerdict.Inconclusive"/> or
/// <see cref="SettlementVerdict.AmbiguousDeparture"/> — both of which park the
/// payout rather than release or reverse it.
/// </para>
/// </remarks>
public sealed class SettlementVerificationService : ISettlementVerificationService
{
    private readonly AppDbContext _db;
    private readonly ISteamInventoryReader _inventory;
    private readonly ILogger<SettlementVerificationService> _logger;

    public SettlementVerificationService(
        AppDbContext db,
        ISteamInventoryReader inventory,
        ILogger<SettlementVerificationService> logger)
    {
        _db = db;
        _inventory = inventory;
        _logger = logger;
    }

    public async Task<SettlementVerificationResult> VerifyAsync(
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // Fresh on both reads, never cached: this round decides whether a payout
        // is released or a refund is raised, and the sidecar's 120-second cache
        // can still show an item that moved two minutes ago (08 §2.3).
        const InventoryReadFreshness Freshness = InventoryReadFreshness.Fresh;

        var buyerSteamId = transaction.BuyerId is { } buyerId
            ? await ResolveSteamIdAsync(buyerId, cancellationToken)
            : null;

        if (buyerSteamId is null)
        {
            return Inconclusive(
                buyerVisibility: null,
                "Alıcının Steam hesabı çözülemedi — mutabakat kontrolü yapılamadı (08 §2.3).");
        }

        // ---------- Buyer side ----------
        var buyerSide = await ReadBuyerSideAsync(transaction, buyerSteamId, Freshness, cancellationToken);

        if (buyerSide.HoldsItem is null)
        {
            return Inconclusive(buyerSide.Visibility, buyerSide.Detail);
        }

        if (buyerSide.HoldsItem is true)
        {
            // The trade stands. The seller's inventory is not read at all here:
            // there is nothing to disambiguate, and the read would spend a
            // rate-limited round trip on a question already answered.
            return new SettlementVerificationResult(
                Verdict: SettlementVerdict.Verified,
                BuyerHoldsItem: true,
                SellerAssetReturned: null,
                BuyerVisibility: buyerSide.Visibility,
                SellerVisibility: null,
                ObservedClassCount: buyerSide.ObservedClassCount,
                ExpectedClassCount: buyerSide.ExpectedClassCount,
                Detail: buyerSide.Detail);
        }

        // ---------- Seller side (only when the item left the buyer) ----------
        var sellerSteamId = await ResolveSteamIdAsync(transaction.SellerId, cancellationToken);
        var sellerRead = sellerSteamId is null
            ? InventoryLookupResult.Unavailable
            : await _inventory.GetItemAsync(
                sellerSteamId, transaction.ItemAssetId, Freshness, cancellationToken);

        // Only a PUBLIC read carries information. "Asset present again" is the
        // reversal signature; "asset absent" is NOT proof of an onward sale,
        // because a reversal may well hand the item back under a rotated id
        // (T122 could not measure a real reversal — runbook §7). So the absent
        // case falls through to AmbiguousDeparture, where a human decides.
        var sellerSideKnown = sellerRead.Visibility == InventoryVisibility.Public;
        var sellerAssetReturned = sellerSideKnown ? sellerRead.Item is not null : (bool?)null;

        if (sellerAssetReturned is true)
        {
            _logger.LogWarning(
                "Transaction {TransactionId}: settlement check found the item gone from the buyer "
                + "and asset {AssetId} back with the seller — trade reversal signature (02 §4.5.1)",
                transaction.Id, transaction.ItemAssetId);

            return new SettlementVerificationResult(
                Verdict: SettlementVerdict.ReversalSignature,
                BuyerHoldsItem: false,
                SellerAssetReturned: true,
                BuyerVisibility: buyerSide.Visibility,
                SellerVisibility: sellerRead.Visibility,
                ObservedClassCount: buyerSide.ObservedClassCount,
                ExpectedClassCount: buyerSide.ExpectedClassCount,
                Detail: $"{buyerSide.Detail} Satıcının orijinal asset'i ({transaction.ItemAssetId}) "
                    + "envanterine geri dönmüş — trade geri alma imzası.");
        }

        return new SettlementVerificationResult(
            Verdict: SettlementVerdict.AmbiguousDeparture,
            BuyerHoldsItem: false,
            SellerAssetReturned: sellerAssetReturned,
            BuyerVisibility: buyerSide.Visibility,
            SellerVisibility: sellerRead.Visibility,
            ObservedClassCount: buyerSide.ObservedClassCount,
            ExpectedClassCount: buyerSide.ExpectedClassCount,
            Detail: $"{buyerSide.Detail} Satıcı tarafı geri dönüşü doğrulamıyor "
                + $"(görünürlük: {sellerRead.Visibility}) — geri alma mı, alıcının devri mi "
                + "ayırt edilemedi; insan incelemesi gerekiyor.");
    }

    /// <summary>
    /// Establish whether the buyer still holds what was delivered.
    /// </summary>
    private async Task<BuyerSideRead> ReadBuyerSideAsync(
        Transaction transaction,
        string buyerSteamId,
        InventoryReadFreshness freshness,
        CancellationToken cancellationToken)
    {
        // ---- Exact route: the asset id recorded at delivery ----
        if (!string.IsNullOrEmpty(transaction.DeliveredBuyerAssetId))
        {
            var read = await _inventory.GetItemAsync(
                buyerSteamId, transaction.DeliveredBuyerAssetId, freshness, cancellationToken);

            if (read.Visibility != InventoryVisibility.Public)
            {
                return new BuyerSideRead(
                    HoldsItem: null,
                    Visibility: read.Visibility,
                    ObservedClassCount: null,
                    ExpectedClassCount: null,
                    Detail: $"Alıcı envanteri okunamadı (görünürlük: {read.Visibility}).");
            }

            var present = read.Item is not null;
            return new BuyerSideRead(
                HoldsItem: present,
                Visibility: read.Visibility,
                ObservedClassCount: null,
                ExpectedClassCount: null,
                Detail: present
                    ? $"Teslim edilen asset ({transaction.DeliveredBuyerAssetId}) hâlâ alıcının envanterinde."
                    : $"Teslim edilen asset ({transaction.DeliveredBuyerAssetId}) alıcının envanterinde yok.");
        }

        // ---- Count route: no recorded asset id (buyer-confirmed delivery) ----
        // Needs the SELLER_CONFIRMED baseline as its reference. A NULL baseline
        // is not a zero baseline (06 §3.5) — without it there is no count to
        // measure against and the buyer side is simply unknowable.
        if (transaction.BuyerBaselineCapturedAt is null || transaction.BuyerBaselineClassCount is not { } baseline)
        {
            return new BuyerSideRead(
                HoldsItem: null,
                Visibility: null,
                ObservedClassCount: null,
                ExpectedClassCount: null,
                Detail: "Teslim edilen asset ID'si kaydedilmemiş ve alıcı baseline'ı yok — "
                    + "envanter üzerinden mutabakat kontrolü yapılamıyor (06 §3.5).");
        }

        var expected = baseline + 1;
        var countRead = await _inventory.CaptureClassBaselineAsync(
            buyerSteamId,
            transaction.ItemClassId,
            transaction.ItemInstanceId,
            freshness,
            cancellationToken);

        if (countRead.Visibility != InventoryVisibility.Public)
        {
            return new BuyerSideRead(
                HoldsItem: null,
                Visibility: countRead.Visibility,
                ObservedClassCount: null,
                ExpectedClassCount: expected,
                Detail: $"Alıcı envanteri okunamadı (görünürlük: {countRead.Visibility}).");
        }

        // 02 §9.2's counting rule, run backwards: delivery raised the buyer's
        // count of this class to at least baseline + 1, so the settlement test
        // is whether it is still at least that. Deliberately >= rather than ==:
        // the buyer may legitimately have acquired further copies of the same
        // skin during an eight-day window, and that must not read as a reversal.
        var stillThere = countRead.ClassCount >= expected;
        return new BuyerSideRead(
            HoldsItem: stillThere,
            Visibility: countRead.Visibility,
            ObservedClassCount: countRead.ClassCount,
            ExpectedClassCount: expected,
            Detail: stillThere
                ? $"Alıcının {transaction.ItemClassId} sınıfı sayımı {countRead.ClassCount} ≥ beklenen {expected} — item duruyor."
                : $"Alıcının {transaction.ItemClassId} sınıfı sayımı {countRead.ClassCount} < beklenen {expected} — item ayrılmış.");
    }

    private static SettlementVerificationResult Inconclusive(
        InventoryVisibility? buyerVisibility, string detail) =>
        new(
            Verdict: SettlementVerdict.Inconclusive,
            BuyerHoldsItem: null,
            SellerAssetReturned: null,
            BuyerVisibility: buyerVisibility,
            SellerVisibility: null,
            ObservedClassCount: null,
            ExpectedClassCount: null,
            Detail: detail);

    private async Task<string?> ResolveSteamIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var steamId = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SteamId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(steamId)) return steamId;

        // Mirrors DeliveryVerificationService: an unresolvable Steam account is
        // absence of information, exactly like an unreachable sidecar — never
        // evidence that an item is missing.
        _logger.LogWarning(
            "Settlement verification could not resolve a Steam ID for user {UserId} — "
            + "that side reads as unavailable (08 §2.3)", userId);
        return null;
    }

    private sealed record BuyerSideRead(
        bool? HoldsItem,
        InventoryVisibility? Visibility,
        int? ObservedClassCount,
        int? ExpectedClassCount,
        string Detail);
}
