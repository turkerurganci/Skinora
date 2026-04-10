using Skinora.Shared.Domain;

namespace Skinora.Users.Domain.Entities;

/// <summary>
/// IP and device fingerprint recording for multi-account detection
/// and security auditing. All fields per 06 §3.2.
/// </summary>
public class UserLoginLog : ISoftDeletable
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string? DeviceFingerprint { get; set; }
    public string? UserAgent { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // --- Timestamp (immutable log — no UpdatedAt) ---
    public DateTime CreatedAt { get; set; }

    // --- Navigation ---
    public User User { get; set; } = null!;
}
