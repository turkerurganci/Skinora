using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Steam.Domain.Entities;

/// <summary>
/// Steam trade offer lifecycle tracking. Each record represents a single trade
/// offer sent through a platform bot to a seller or buyer.
/// All fields per 06 §3.9.
/// </summary>
public class TradeOffer : BaseEntity, IAuditableEntity
{
    // --- Relationships ---
    public Guid TransactionId { get; set; }
    public Guid PlatformSteamBotId { get; set; }

    // --- Trade Offer ---
    public TradeOfferDirection Direction { get; set; }
    public string? SteamTradeOfferId { get; set; }
    public TradeOfferStatus Status { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }

    // --- Timestamps ---
    public DateTime? SentAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}
