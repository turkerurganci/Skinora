using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Steam.Domain.Entities;

/// <summary>
/// Platform Steam bot accounts and their operational status.
/// Bot selection uses capacity-based routing — lowest ActiveEscrowCount
/// among active bots (05 §3.2).
/// All fields per 06 §3.10.
/// </summary>
public class PlatformSteamBot : BaseEntity, ISoftDeletable, IAuditableEntity
{
    public string SteamId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public PlatformSteamBotStatus Status { get; set; }

    // --- Denormalized counters ---
    public int ActiveEscrowCount { get; set; }
    public int DailyTradeOfferCount { get; set; }

    // --- Health ---
    public DateTime? LastHealthCheckAt { get; set; }

    // --- ISoftDeletable ---
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
