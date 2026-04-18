using Skinora.Shared.Domain;

namespace Skinora.Admin.Domain.Entities;

/// <summary>
/// Admin role definition. All fields per 06 §3.14.
/// </summary>
public class AdminRole : BaseEntity, ISoftDeletable, IAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSuperAdmin { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
