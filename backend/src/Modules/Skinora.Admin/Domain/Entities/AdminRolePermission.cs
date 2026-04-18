using Skinora.Shared.Domain;

namespace Skinora.Admin.Domain.Entities;

/// <summary>
/// Per-role permission assignment. All fields per 06 §3.15.
/// </summary>
public class AdminRolePermission : BaseEntity, ISoftDeletable, IAuditableEntity
{
    public Guid AdminRoleId { get; set; }
    public string Permission { get; set; } = string.Empty;

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
