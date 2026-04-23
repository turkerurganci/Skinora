using Skinora.Shared.Domain;

namespace Skinora.Users.Domain.Entities;

/// <summary>
/// Platform user — authenticated via Steam OpenID.
/// All fields per 06 §3.1.
/// </summary>
public class User : BaseEntity, ISoftDeletable, IAuditableEntity
{
    // --- Steam identity ---
    public string SteamId { get; set; } = string.Empty;
    public string SteamDisplayName { get; set; } = string.Empty;
    public string? SteamAvatarUrl { get; set; }

    // --- Wallet addresses (TRC-20) ---
    public string? DefaultPayoutAddress { get; set; }
    public string? DefaultRefundAddress { get; set; }
    public DateTime? PayoutAddressChangedAt { get; set; }
    public DateTime? RefundAddressChangedAt { get; set; }

    // --- Profile ---
    public string? Email { get; set; }
    public string PreferredLanguage { get; set; } = "en";

    // --- Terms of Service ---
    public string? TosAcceptedVersion { get; set; }
    public DateTime? TosAcceptedAt { get; set; }

    // --- Age gate (18+ self-attestation — 02 §21.1, 03 §11a.2) ---
    public DateTime? AgeConfirmedAt { get; set; }

    // --- Steam verification ---
    public bool MobileAuthenticatorVerified { get; set; }

    // --- Reputation (denormalized) ---
    public int CompletedTransactionCount { get; set; }
    public decimal? SuccessfulTransactionRate { get; set; }

    // --- Cooldown ---
    public DateTime? CooldownExpiresAt { get; set; }

    // --- Account state ---
    public bool IsDeactivated { get; set; }
    public DateTime? DeactivatedAt { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // --- Navigation properties ---
    public ICollection<UserLoginLog> LoginLogs { get; set; } = [];
}
