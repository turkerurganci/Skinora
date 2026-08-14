using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Steam;

namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// What one delivery-verification round concluded (02 §9.2).
/// </summary>
/// <remarks>
/// <para>
/// The verdict is deliberately separate from <see cref="Evidence"/>. Evidence
/// flags record <em>what was observed</em>; the verdict records <em>what may be
/// done about it</em>, which additionally depends on how much of the inventory
/// could be read and on the launch gate (DEPLOY_RUNBOOK §H). Reading
/// <c>DeliveryEvidence.IsSufficientForDelivery()</c> alone is not enough for a
/// caller: see <see cref="DeliveryVerdict.Inconclusive"/>.
/// </para>
/// </remarks>
public sealed record DeliveryVerificationResult
{
    internal DeliveryVerificationResult(
        DeliveryVerdict verdict,
        DeliveryEvidence evidence,
        DeliveryEvidence observedEvidence,
        InventoryVisibility? sellerVisibility,
        InventoryVisibility? buyerVisibility,
        bool baselineAvailable,
        int? baselineClassCount,
        int? observedClassCount,
        string? candidateDeliveredAssetId,
        bool autoReleaseGated,
        DeliveryEvidenceCaptureData? capture)
    {
        Verdict = verdict;
        Evidence = evidence;
        ObservedEvidence = observedEvidence;
        SellerVisibility = sellerVisibility;
        BuyerVisibility = buyerVisibility;
        BaselineAvailable = baselineAvailable;
        BaselineClassCount = baselineClassCount;
        ObservedClassCount = observedClassCount;
        CandidateDeliveredAssetId = candidateDeliveredAssetId;
        AutoReleaseGated = autoReleaseGated;
        Capture = capture;
    }

    /// <summary>The decision a caller acts on.</summary>
    public DeliveryVerdict Verdict { get; }

    /// <summary>
    /// The evidence already recorded on the transaction, merged with whatever
    /// this round observed. This is the value a caller persists onto
    /// <c>Transaction.DeliveryEvidence</c>.
    /// </summary>
    public DeliveryEvidence Evidence { get; }

    /// <summary>
    /// Only the flags <em>this round</em> raised from the inventories. Never
    /// includes <see cref="DeliveryEvidence.BUYER_CONFIRMED"/> — the buyer's
    /// confirmation is recorded by the confirm-receipt endpoint, not observed
    /// here.
    /// </summary>
    public DeliveryEvidence ObservedEvidence { get; }

    /// <summary>How the seller's inventory read ended; <c>null</c> when it was not read.</summary>
    public InventoryVisibility? SellerVisibility { get; }

    /// <summary>How the buyer's inventory read ended; <c>null</c> when it was not read.</summary>
    public InventoryVisibility? BuyerVisibility { get; }

    /// <summary>
    /// Whether a 06 §3.5 baseline exists at all. <c>false</c> closes the
    /// inventory-evidence path outright: with no reference snapshot there is
    /// nothing for a count to be measured against (02 §9.2).
    /// </summary>
    public bool BaselineAvailable { get; }

    /// <summary>The baseline count the delta was measured against, when there was one.</summary>
    public int? BaselineClassCount { get; }

    /// <summary>The count observed in this round, when the buyer's inventory was read.</summary>
    public int? ObservedClassCount { get; }

    /// <summary>
    /// The asset ID that appeared in the buyer's inventory since the baseline,
    /// when exactly one did. Best-effort audit material for
    /// <c>Transaction.DeliveredBuyerAssetId</c> (06 §8.4) — never a guard.
    /// </summary>
    public string? CandidateDeliveredAssetId { get; }

    /// <summary>
    /// <c>true</c> when the inventory path produced sufficient evidence but the
    /// launch gate is still closed, so money must not move automatically. Always
    /// <c>false</c> once evidence includes
    /// <see cref="DeliveryEvidence.BUYER_CONFIRMED"/> — the gate governs the
    /// platform's inference, never the buyer's own decision.
    /// </summary>
    public bool AutoReleaseGated { get; }

    /// <summary>
    /// The audit snapshot to persist for launch-gate review, or <c>null</c> when
    /// this round produced nothing worth reviewing.
    /// </summary>
    public DeliveryEvidenceCaptureData? Capture { get; }
}

/// <summary>
/// The five outcomes of a delivery-verification round.
/// </summary>
/// <remarks>
/// The split that matters for money safety is
/// <see cref="MisdeliverySignature"/> vs <see cref="Inconclusive"/>. Both leave
/// the transaction undelivered, but the first is a positive finding about a
/// seller and the second is an admission that the platform could not look.
/// </remarks>
public enum DeliveryVerdict
{
    /// <summary>
    /// Evidence is sufficient under 02 §9.2 and nothing gates it. The caller may
    /// fire <c>DeliverItem</c>.
    /// </summary>
    Delivered,

    /// <summary>
    /// The inventory conjunction held, but the launch gate is closed
    /// (DEPLOY_RUNBOOK §H). The finding is real and must be reviewed by a human
    /// before it releases money — it must NOT be turned into a cancellation
    /// either, since the evidence says the item arrived.
    /// </summary>
    InventoryEvidencePendingReview,

    /// <summary>
    /// The seller's asset left their inventory and the buyer's count did not
    /// rise — both sides read successfully. Wrong item, or a send to a third
    /// party. Escalates to an admin; never resolves silently (02 §10.1).
    /// </summary>
    MisdeliverySignature,

    /// <summary>
    /// Both sides were read and neither moved: the seller has not sent yet.
    /// </summary>
    NoMovement,

    /// <summary>
    /// At least one side could not be read (private / unreachable / no
    /// baseline), and what <em>was</em> read does not settle the question.
    /// Absence of information — never a negative finding (08 §2.3).
    /// </summary>
    Inconclusive,
}

/// <summary>
/// The audit snapshot behind one verification round — what a reviewer needs to
/// answer the three questions T122 could not measure without a real trade
/// (runbook §7: delivery latency, asset-ID rotation, Item Certificate
/// persistence).
/// </summary>
/// <remarks>
/// Serialized into <c>DeliveryEvidenceCapture.Payload</c> by
/// <see cref="DeliveryEvidenceCaptureRecorder"/>. Scoped to the transaction's
/// own item class on both sides — never a dump of either party's inventory.
/// </remarks>
public sealed record DeliveryEvidenceCaptureData(
    DateTime ObservedAt,
    string ItemClassId,
    string? ItemInstanceId,
    string SellerItemAssetId,
    string? SellerVisibility,
    bool SellerAssetPresent,
    IReadOnlyList<InventoryAssetProperty> SellerAssetProperties,
    string? BuyerVisibility,
    int? BaselineClassCount,
    DateTime? BaselineCapturedAt,
    IReadOnlyList<string> BaselineAssetIds,
    bool BaselineAssetIdsTruncated,
    int? ObservedClassCount,
    IReadOnlyList<InventoryClassAsset> ObservedAssets,
    IReadOnlyList<string> NewAssetIds,
    DateTime? PaymentReceivedAt,
    DateTime? DeliveryDeadline);
