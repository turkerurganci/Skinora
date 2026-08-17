using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Disputes.Domain.Entities;

/// <summary>
/// Buyer-initiated dispute record tied to a transaction.
/// Unfiltered unique on (TransactionId + Type) — same type cannot be reopened (02 §10.2).
/// All fields per 06 §3.11.
/// </summary>
public class Dispute : BaseEntity, ISoftDeletable, IAuditableEntity
{
    // --- Relationships ---
    public Guid TransactionId { get; set; }
    public Guid OpenedByUserId { get; set; }
    public Guid? AdminId { get; set; }

    // --- Dispute ---
    public DisputeType Type { get; set; }
    public DisputeStatus Status { get; set; }
    public string? SystemCheckResult { get; set; }
    public string? UserDescription { get; set; }
    public string? AdminNote { get; set; }

    /// <summary>
    /// T130 — the Steam name of the item that actually arrived, when a WRONG_ITEM
    /// auto-check established that it was not the one in the transaction
    /// (02 §10.1 third row: "gelen item'ın adı kayda geçirilerek admin'e
    /// yükseltilir"). NULL on every other dispute and on a wrong-item case the
    /// platform could not read.
    /// </summary>
    /// <remarks>
    /// A column rather than a sentence inside <see cref="SystemCheckResult"/>:
    /// that field is localized to the buyer's language at produce time, and the
    /// admin who reads it may not share it. The evidence an admin acts on must
    /// not be embedded in a translation.
    /// </remarks>
    public string? DeliveredItemName { get; set; }

    // --- Resolution ---
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// T131 — why an admin ruled for the buyer on a transaction whose delivery
    /// the platform had already established (02 §10.4, 03 §6.4). NULL on every
    /// other resolution, including a buyer-favour ruling on an undelivered
    /// transaction, where the ruling is the ordinary outcome rather than an
    /// exception.
    /// </summary>
    /// <remarks>
    /// A column of its own rather than a longer <see cref="AdminNote"/>: 03
    /// §6.4 requires the justification to be recorded <em>separately</em>, and
    /// the two answer different questions. The note explains the case to
    /// whoever reads the dispute; this field answers "why was the platform's
    /// own proof of delivery overruled" — the question an audit asks after the
    /// money is gone and the seller has no way to recover the item.
    /// </remarks>
    public string? ResolutionOverrideReason { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
