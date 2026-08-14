using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Steam;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// T125 — default <see cref="IDeliveryVerificationService"/>. Implements the
/// 02 §9.2 evidence rules over the two inventory reads the platform is allowed
/// to make.
/// </summary>
/// <remarks>
/// <para>
/// The platform is not a party to the seller→buyer trade, so Steam never tells
/// it "the offer was accepted" (02 §9.2). Delivery is therefore inferred, and
/// the inference is deliberately conservative in one direction only: every
/// branch that cannot prove delivery leaves the transaction where it is, and
/// every branch that cannot prove <em>non</em>-delivery says so explicitly
/// rather than defaulting to "not delivered".
/// </para>
/// <para>
/// <b>What this service never reads.</b> Lock state. Not
/// <c>market_tradable_restriction</c> (T122-B8: the field carries the item
/// class's policy and reads 7 for a freely tradable copy), and not
/// <c>IsTradeable</c> either (runbook §6: the flag is class-level and carries
/// no expiry in an anonymous read, so a cooldown has no signature the platform
/// can observe). T122 could not measure how a locked item looks anonymously,
/// and an unmeasured signal must not end up wired to money movement — so it is
/// excluded from the design rather than guessed at.
/// </para>
/// </remarks>
public sealed class DeliveryVerificationService : IDeliveryVerificationService
{
    /// <summary>
    /// The launch gate (DEPLOY_RUNBOOK §H). While this is false, evidence
    /// gathered from inventories is recorded and surfaced but does not release
    /// money on its own.
    /// </summary>
    public const string AutoReleaseSettingKey = "delivery.inventory_evidence_auto_release_enabled";

    private readonly AppDbContext _db;
    private readonly ISteamInventoryReader _inventory;
    private readonly ILogger<DeliveryVerificationService> _logger;
    private readonly TimeProvider _clock;

    public DeliveryVerificationService(
        AppDbContext db,
        ISteamInventoryReader inventory,
        ILogger<DeliveryVerificationService> logger,
        TimeProvider clock)
    {
        _db = db;
        _inventory = inventory;
        _logger = logger;
        _clock = clock;
    }

    public async Task<DeliveryVerificationResult> VerifyAsync(
        Transaction transaction,
        InventoryReadFreshness freshness,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var recorded = transaction.DeliveryEvidence;

        // A NULL baseline is not a zero baseline (06 §3.5): zero is a claim
        // ("the buyer holds none of this skin") that a delta would later be
        // measured against, while NULL says the snapshot was never taken.
        var baselineAvailable =
            transaction.BuyerBaselineCapturedAt is not null
            && transaction.BuyerBaselineClassCount is not null;

        // ---------- Short circuit: the buyer already confirmed ----------
        // 02 §9.2 — buyer confirmation is sufficient ON ITS OWN and cannot be
        // overturned by an inventory read: the confirmation runs against the
        // buyer's own interest (it releases their money), so there is no
        // incentive to claim it falsely. Reading Steam here could only produce
        // a weaker signal that argues with a stronger one, at the cost of two
        // rate-limited round trips per poll.
        if (recorded.HasFlag(DeliveryEvidence.BUYER_CONFIRMED))
        {
            return new DeliveryVerificationResult(
                verdict: DeliveryVerdict.Delivered,
                evidence: recorded,
                observedEvidence: DeliveryEvidence.NONE,
                sellerVisibility: null,
                buyerVisibility: null,
                baselineAvailable: baselineAvailable,
                baselineClassCount: transaction.BuyerBaselineClassCount,
                observedClassCount: null,
                candidateDeliveredAssetId: null,
                autoReleaseGated: false,
                capture: null);
        }

        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        // ---------- Seller side: is ItemAssetId still theirs? ----------
        // Asset-ID matching is valid here and ONLY here. The item has not moved
        // from the seller's side, so the ID the platform snapshotted at creation
        // is still the ID Steam uses. On the buyer's side it is worthless —
        // Steam rotates the ID on every trade (06 §8.4).
        var sellerSteamId = await ResolveSteamIdAsync(transaction.SellerId, cancellationToken);
        var sellerRead = sellerSteamId is null
            ? InventoryLookupResult.Unavailable
            : await _inventory.GetItemAsync(
                sellerSteamId, transaction.ItemAssetId, freshness, cancellationToken);

        var sellerAssetGone =
            sellerRead.Visibility == InventoryVisibility.Public && sellerRead.Item is null;
        var sellerSideKnown = sellerRead.Visibility == InventoryVisibility.Public;

        // ---------- Buyer side: did the class count rise? ----------
        // With no baseline the read is skipped outright: it would produce a
        // count with nothing to compare against, and comparing against zero
        // would read every copy the buyer already owned as a fresh delivery.
        InventoryClassBaselineResult? buyerRead = null;
        if (baselineAvailable)
        {
            // BuyerId is nullable — a CREATED invitation has no buyer yet. It
            // cannot be null here in practice (the baseline is taken on entry to
            // SELLER_CONFIRMED, which requires one), but "no buyer" resolves to
            // an unreadable side rather than to an empty inventory.
            var buyerSteamId = transaction.BuyerId is { } buyerId
                ? await ResolveSteamIdAsync(buyerId, cancellationToken)
                : null;
            buyerRead = buyerSteamId is null
                ? InventoryClassBaselineResult.Unavailable
                : await _inventory.CaptureClassBaselineAsync(
                    buyerSteamId,
                    transaction.ItemClassId,
                    transaction.ItemInstanceId,
                    freshness,
                    cancellationToken);
        }

        var buyerSideKnown = buyerRead is { Visibility: InventoryVisibility.Public };
        int? observedClassCount = buyerSideKnown ? buyerRead!.ClassCount : null;

        // 02 §9.2 is a COUNTING rule, not a set-membership rule: one class
        // legitimately appears many times in the same inventory (T122 measured a
        // class with 9 copies), so "does the buyer own this skin" would never
        // see a delivery into an inventory that already held one.
        var inventoryDelta =
            buyerSideKnown && observedClassCount > transaction.BuyerBaselineClassCount;

        var observed = DeliveryEvidence.NONE;
        if (sellerAssetGone) observed |= DeliveryEvidence.SELLER_ASSET_GONE;
        if (inventoryDelta) observed |= DeliveryEvidence.INVENTORY_DELTA;

        var evidence = recorded | observed;

        // ---------- Verdict ----------
        var gateOpen = await IsAutoReleaseEnabledAsync(cancellationToken);
        var verdict = Decide(evidence, sellerSideKnown, buyerSideKnown, baselineAvailable, gateOpen);
        var gated = verdict == DeliveryVerdict.InventoryEvidencePendingReview;

        var baselineAssetIds = DeserializeBaselineAssetIds(transaction);
        var newAssetIds = ResolveNewAssetIds(buyerRead, baselineAssetIds, transaction);

        // 06 §8.4 — best-effort only. Ambiguity resolves to null rather than to
        // a guess: the column feeds WRONG_ITEM dispute handling (02 §10.1), and
        // naming the wrong asset there is worse than naming none.
        var candidate = newAssetIds.Count == 1 ? newAssetIds[0] : null;

        var capture = BuildCapture(
            transaction, nowUtc, sellerRead, buyerRead, baselineAssetIds,
            observedClassCount, newAssetIds, verdict, gateOpen);

        if (verdict == DeliveryVerdict.MisdeliverySignature)
        {
            _logger.LogWarning(
                "Transaction {TransactionId}: seller asset {AssetId} left their inventory but the "
                + "buyer's {ClassId} count did not rise ({Observed} vs baseline {Baseline}) — "
                + "misdelivery signature (02 §10.1)",
                transaction.Id, transaction.ItemAssetId, transaction.ItemClassId,
                observedClassCount, transaction.BuyerBaselineClassCount);
        }
        else if (gated)
        {
            _logger.LogWarning(
                "Transaction {TransactionId}: inventory evidence is sufficient but the launch gate "
                + "'{SettingKey}' is closed — held for human review (DEPLOY_RUNBOOK §H)",
                transaction.Id, AutoReleaseSettingKey);
        }

        return new DeliveryVerificationResult(
            verdict: verdict,
            evidence: evidence,
            observedEvidence: observed,
            sellerVisibility: sellerRead.Visibility,
            buyerVisibility: buyerRead?.Visibility,
            baselineAvailable: baselineAvailable,
            baselineClassCount: transaction.BuyerBaselineClassCount,
            observedClassCount: observedClassCount,
            candidateDeliveredAssetId: candidate,
            autoReleaseGated: gated,
            capture: capture);
    }

    /// <summary>
    /// The 02 §9.2 decision table, expressed once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Order matters. Sufficiency is tested first because it is the only branch
    /// that can be established from partial information: if both inventory bits
    /// are set, it makes no difference that one side was unreadable on some
    /// earlier round.
    /// </para>
    /// <para>
    /// The misdelivery branch is the opposite — it is a claim about a seller, so
    /// it demands that BOTH sides were actually read this round. "Seller's asset
    /// is gone and the buyer's inventory is private" is not a misdelivery
    /// signature; it is the platform being unable to look (08 §2.3).
    /// </para>
    /// </remarks>
    private static DeliveryVerdict Decide(
        DeliveryEvidence evidence,
        bool sellerSideKnown,
        bool buyerSideKnown,
        bool baselineAvailable,
        bool gateOpen)
    {
        if (evidence.IsSufficientForDelivery())
        {
            // The gate governs the platform's INFERENCE from inventories. A
            // buyer-confirmed delivery is the buyer's own decision and is never
            // gated (the short circuit above already returned, so reaching here
            // with BUYER_CONFIRMED means it was recorded alongside inventory
            // evidence in the same round — still the buyer's decision).
            if (evidence.HasFlag(DeliveryEvidence.BUYER_CONFIRMED) || gateOpen)
                return DeliveryVerdict.Delivered;

            return DeliveryVerdict.InventoryEvidencePendingReview;
        }

        if (evidence.IsMisdeliverySignature() && sellerSideKnown && buyerSideKnown)
            return DeliveryVerdict.MisdeliverySignature;

        // Nothing moved — but only if the platform could actually see both
        // sides. Without a baseline the buyer's half was never even attempted,
        // so "no movement" would be a claim the platform cannot support.
        if (sellerSideKnown && buyerSideKnown && baselineAvailable)
            return DeliveryVerdict.NoMovement;

        return DeliveryVerdict.Inconclusive;
    }

    /// <summary>
    /// Read the launch gate. Anything other than a configured, parsable
    /// <c>true</c> keeps the gate CLOSED.
    /// </summary>
    /// <remarks>
    /// Fail-closed is the cheap direction here: a wrongly closed gate delays a
    /// payout until a human looks, while a wrongly open one releases money on
    /// an inference nobody has validated against a real trade yet (T122 runbook
    /// §7).
    /// </remarks>
    private async Task<bool> IsAutoReleaseEnabledAsync(CancellationToken cancellationToken)
    {
        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == AutoReleaseSettingKey && s.IsConfigured)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return bool.TryParse(raw, out var enabled) && enabled;
    }

    private async Task<string?> ResolveSteamIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var steamId = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.SteamId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(steamId)) return steamId;

        // An unresolvable Steam account is absence of information, exactly like
        // an unreachable sidecar — never evidence that an item is missing.
        _logger.LogWarning(
            "Delivery verification could not resolve a Steam ID for user {UserId} — "
            + "that side reads as unavailable (08 §2.3)", userId);
        return null;
    }

    /// <summary>
    /// The asset IDs recorded at baseline. Empty when the column is absent or
    /// unparsable, which degrades <see cref="ResolveNewAssetIds"/> to "cannot
    /// name the new asset" rather than to a wrong name.
    /// </summary>
    private IReadOnlyList<string> DeserializeBaselineAssetIds(Transaction transaction)
    {
        if (string.IsNullOrWhiteSpace(transaction.BuyerBaselineAssetIds)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(transaction.BuyerBaselineAssetIds) ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Transaction {TransactionId} has an unparsable BuyerBaselineAssetIds column — "
                + "the delivered asset id cannot be identified (06 §3.5)", transaction.Id);
            return [];
        }
    }

    /// <summary>
    /// Which asset IDs are in the buyer's inventory now but were not at baseline.
    /// </summary>
    /// <remarks>
    /// Returns empty when the stored ID list was truncated (T123 caps the column
    /// at 400 characters). A truncated baseline makes every ID it dropped look
    /// new, so the diff would invent arrivals. The delivery decision itself is
    /// unaffected — that is decided by <c>BuyerBaselineClassCount</c>, which is
    /// always the exact untruncated count.
    /// </remarks>
    private static IReadOnlyList<string> ResolveNewAssetIds(
        InventoryClassBaselineResult? buyerRead,
        IReadOnlyList<string> baselineAssetIds,
        Transaction transaction)
    {
        if (buyerRead is not { Visibility: InventoryVisibility.Public }) return [];
        if (IsBaselineTruncated(baselineAssetIds, transaction)) return [];

        var known = new HashSet<string>(baselineAssetIds, StringComparer.Ordinal);
        return [.. buyerRead.AssetIds.Where(id => !known.Contains(id))];
    }

    private static bool IsBaselineTruncated(
        IReadOnlyList<string> baselineAssetIds, Transaction transaction)
        => transaction.BuyerBaselineClassCount is { } count && baselineAssetIds.Count < count;

    /// <summary>
    /// Build the launch-gate audit snapshot (DEPLOY_RUNBOOK §H).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Produced only for rounds a reviewer would actually read: a delivery
    /// concluded from inventories (gated or not) and the misdelivery signature.
    /// A poll that found nothing is not evidence about anything, and capturing
    /// every one of them would bury the rows that matter.
    /// </para>
    /// <para>
    /// Scope is the transaction's own item class on both sides plus the
    /// timestamps. It is deliberately NOT a dump of either party's inventory:
    /// the questions the capture exists to answer (runbook §7 B1–B3 — latency,
    /// asset-ID rotation, Item Certificate persistence) are all about this one
    /// item, and third-party inventory contents are personal data (runbook §8).
    /// </para>
    /// </remarks>
    private static DeliveryEvidenceCaptureData? BuildCapture(
        Transaction transaction,
        DateTime nowUtc,
        InventoryLookupResult sellerRead,
        InventoryClassBaselineResult? buyerRead,
        IReadOnlyList<string> baselineAssetIds,
        int? observedClassCount,
        IReadOnlyList<string> newAssetIds,
        DeliveryVerdict verdict,
        bool gateOpen)
    {
        var worthCapturing = verdict is DeliveryVerdict.InventoryEvidencePendingReview
            or DeliveryVerdict.MisdeliverySignature
            || (verdict == DeliveryVerdict.Delivered && gateOpen);
        if (!worthCapturing) return null;

        return new DeliveryEvidenceCaptureData(
            ObservedAt: nowUtc,
            ItemClassId: transaction.ItemClassId,
            ItemInstanceId: transaction.ItemInstanceId,
            SellerItemAssetId: transaction.ItemAssetId,
            SellerVisibility: sellerRead.Visibility.ToString(),
            SellerAssetPresent: sellerRead.Item is not null,
            SellerAssetProperties: sellerRead.Item?.AssetProperties ?? [],
            BuyerVisibility: buyerRead?.Visibility.ToString(),
            BaselineClassCount: transaction.BuyerBaselineClassCount,
            BaselineCapturedAt: transaction.BuyerBaselineCapturedAt,
            BaselineAssetIds: baselineAssetIds,
            BaselineAssetIdsTruncated: IsBaselineTruncated(baselineAssetIds, transaction),
            ObservedClassCount: observedClassCount,
            ObservedAssets: buyerRead is { Visibility: InventoryVisibility.Public }
                ? buyerRead.Assets
                : [],
            NewAssetIds: newAssetIds,
            PaymentReceivedAt: transaction.PaymentReceivedAt,
            DeliveryDeadline: transaction.DeliveryDeadline);
    }
}
