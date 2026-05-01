using Skinora.Shared.Enums;

namespace Skinora.Platform.Application.Audit;

/// <summary>
/// Input record for <see cref="IAuditLogger"/>. Mirrors <c>AuditLog</c>
/// (06 §3.20) but excludes <c>Id</c> (IDENTITY) and <c>CreatedAt</c> (the
/// logger stamps it from the injected clock).
/// </summary>
public sealed record AuditLogEntry(
    Guid? UserId,
    Guid ActorId,
    ActorType ActorType,
    AuditAction Action,
    string EntityType,
    string EntityId,
    string? OldValue,
    string? NewValue,
    string? IpAddress);
