using Skinora.Shared.Domain;
using Skinora.Users.Domain.Entities;

namespace Skinora.Auth.Domain.Entities;

/// <summary>
/// JWT refresh token for session management and token rotation.
/// All fields per 06 §3.3.
/// </summary>
public class RefreshToken : BaseEntity, ISoftDeletable, IAuditableEntity
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // --- Token rotation (self-referencing FK) ---
    public Guid? ReplacedByTokenId { get; set; }

    // --- Device/session info ---
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }

    // --- Navigation ---
    public User User { get; set; } = null!;
    public RefreshToken? ReplacedByToken { get; set; }
}
