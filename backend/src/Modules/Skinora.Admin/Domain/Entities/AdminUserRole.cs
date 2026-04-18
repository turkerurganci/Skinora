using Skinora.Shared.Domain;

namespace Skinora.Admin.Domain.Entities;

/// <summary>
/// User-role assignment (N:M). Surrogate PK enables re-assignment after
/// soft delete. All fields per 06 §3.16.
/// </summary>
public class AdminUserRole : BaseEntity, ISoftDeletable, IAuditableEntity
{
    public Guid UserId { get; set; }
    public Guid AdminRoleId { get; set; }
    public DateTime AssignedAt { get; set; }
    public Guid? AssignedByAdminId { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
