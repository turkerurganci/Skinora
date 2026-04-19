using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Platform.Domain.Entities;

/// <summary>
/// Immutable audit trail row. All fields per 06 §3.20.
/// </summary>
/// <remarks>
/// Append-only: INSERT is the only supported operation. UPDATE and DELETE are
/// rejected at the <c>AppDbContext</c> level (06 §3.20, §4.2). The table
/// persists financial and security events beyond Loki retention and is never
/// archived or truncated.
/// </remarks>
public class AuditLog : IAppendOnly
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid ActorId { get; set; }
    public ActorType ActorType { get; set; }
    public AuditAction Action { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
}
