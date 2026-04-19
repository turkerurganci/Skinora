using Skinora.Shared.Domain;

namespace Skinora.Platform.Domain.Entities;

/// <summary>
/// Admin-managed platform parameter. All fields per 06 §3.17.
/// Mutable Catalog (Delete Yasak) — the key set is seeded and only migrations
/// may alter it; admins may only update <see cref="Value"/>.
/// </summary>
public class SystemSetting : BaseEntity, IAuditableEntity
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool IsConfigured { get; set; }
    public string DataType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? UpdatedByAdminId { get; set; }
}
