using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Delivery;

/// <summary>
/// T130 — the fresh delivery-verification round 02 §10.1 requires when a buyer
/// opens a DELIVERY dispute ("§9.2 kanıt kuralları <b>taze olarak</b>
/// çalıştırılır"), and the consequences that follow from its verdict.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this lives in Transactions rather than in the dispute checker.</b>
/// One arm of the round moves the transaction to <c>ITEM_DELIVERED</c>
/// (03 §6.2 Sonuç A), which means the state machine, the settlement-window
/// stamper and the history recorder — all Transactions-side concerns that
/// T126 and T127 already drive through their own callers. The Disputes module
/// keeps what is actually its own: what the buyer is told and whether the
/// dispute closes, escalates or stays open.
/// </para>
/// <para>
/// <b>Unit of work.</b> Writes land on the caller's tracked
/// <c>AppDbContext</c>; nothing here calls <c>SaveChangesAsync</c>. The dispute
/// service owns the save, so the dispute row and any delivery transition it
/// triggered commit together — a buyer must never see a dispute recorded
/// against a delivery that rolled back.
/// </para>
/// <para>
/// <b>What it deliberately does NOT do:</b> raise the misdelivery escalation
/// itself. On this path the dispute row is being created by the caller in the
/// same unit of work, and <c>UQ_Disputes_TransactionId_Type</c> permits exactly
/// one DELIVERY row per transaction — so the round reports the signature and
/// the caller opens the dispute as <c>ESCALATED</c>. The
/// <see cref="IDeliveryMisdeliveryEscalator"/> path stays what it was built
/// for: the timeout scanner, where no dispute exists yet.
/// </para>
/// </remarks>
public interface IDeliveryDisputeRound
{
    /// <summary>
    /// Run one fresh round for a transaction a DELIVERY dispute is being opened
    /// against.
    /// </summary>
    Task<DeliveryDisputeOutcome> RunAsync(
        Transaction transaction,
        CancellationToken cancellationToken);
}

/// <summary>
/// What the round concluded, in the vocabulary 03 §6.2 uses. The five values are
/// exhaustive over the five <see cref="DeliveryVerdict"/> outcomes.
/// </summary>
public enum DeliveryDisputeOutcome
{
    /// <summary>
    /// 03 §6.2 Sonuç A — delivery proven and nothing gates it. The round has
    /// already fired <c>DeliverItem</c> (unless the transaction was past that
    /// point already), written the history row and published the status change.
    /// </summary>
    Delivered,

    /// <summary>
    /// 03 §6.2 Sonuç B — both inventories read, neither moved: the seller has
    /// not sent yet.
    /// </summary>
    NotSent,

    /// <summary>
    /// 03 §6.2 Sonuç C — the seller's asset left their inventory and nothing
    /// arrived for the buyer. A positive finding about a seller; the caller
    /// escalates rather than answering the buyer.
    /// </summary>
    MisdeliverySignature,

    /// <summary>
    /// 03 §6.2 Sonuç D — at least one side could not be read. Absence of
    /// information, never a negative finding (08 §2.3).
    /// </summary>
    Unreadable,

    /// <summary>
    /// 03 §6.2 Sonuç E — the inventory conjunction held but the launch gate is
    /// closed (DEPLOY_RUNBOOK §H), so no money moves on the platform's own
    /// inference until a human has read the capture.
    /// </summary>
    /// <remarks>
    /// This value is why the round exists at all. Before T130 the checker read
    /// <c>DeliveryEvidence.IsSufficientForDelivery()</c> off the transaction and
    /// answered "delivered" — which closed the dispute with
    /// <c>CanEscalate = false</c> on exactly the transactions whose money the
    /// gate was holding. The buyer's funds sat in escrow with the automatic
    /// route gated and the manual route shut.
    /// </remarks>
    PendingReview,
}
