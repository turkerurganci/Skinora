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

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
