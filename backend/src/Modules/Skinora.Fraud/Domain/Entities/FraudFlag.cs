using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Fraud.Domain.Entities;

/// <summary>
/// Fraud detection flag record. Scope determines whether this is an
/// account-level block (TransactionId NULL) or a transaction pre-create
/// hold (TransactionId NOT NULL). All fields per 06 §3.12.
/// </summary>
public class FraudFlag : BaseEntity, ISoftDeletable, IAuditableEntity
{
    // --- Relationships ---
    public Guid? TransactionId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ReviewedByAdminId { get; set; }

    // --- Flag ---
    public FraudFlagScope Scope { get; set; }
    public FraudFlagType Type { get; set; }
    public string Details { get; set; } = string.Empty;
    public ReviewStatus Status { get; set; }
    public string? AdminNote { get; set; }

    // --- Review ---
    public DateTime? ReviewedAt { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
