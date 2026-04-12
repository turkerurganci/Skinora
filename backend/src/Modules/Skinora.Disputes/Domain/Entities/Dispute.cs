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

    // --- Resolution ---
    public DateTime? ResolvedAt { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
