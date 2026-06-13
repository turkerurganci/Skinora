using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the Steam webhook handler (T103b-2 — 02 §15, 03 §11.2a) when a
/// platform bot transitions INTO a durable restriction (RESTRICTED or BANNED).
/// Consumed by <c>BotRestrictionRecoveryConsumer</c>, which materialises a
/// <c>BotRecoveryItem</c> for each transaction whose item is still in that bot's
/// custody and auto-applies an EMERGENCY_HOLD so those transactions' timeouts
/// stop ticking while the item is physically stuck.
/// </summary>
/// <remarks>
/// OFFLINE transitions deliberately do NOT raise this event — a lost session is
/// treated as transient (new transactions are already diverted by the ACTIVE-only
/// bot selector); the per-minute <c>BotHealthCheck</c> escalates a durable failure
/// to restricted/banned, which then fires here. New-transaction diversion needs no
/// event — it is the steady-state behaviour of <c>SqlBotSelectionService</c>.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="PlatformSteamBotId">The bot that became restricted/banned.</param>
/// <param name="SteamId">Bot Steam ID (snapshot for logging/audit).</param>
/// <param name="DisplayName">Bot display name (snapshot).</param>
/// <param name="Status">The new bot status — RESTRICTED or BANNED.</param>
/// <param name="Reason">Sidecar restriction reason that triggered the transition.</param>
/// <param name="OccurredAt">UTC timestamp the transition was committed.</param>
public record BotRestrictedEvent(
    Guid EventId,
    Guid PlatformSteamBotId,
    string SteamId,
    string DisplayName,
    string Status,
    string? Reason,
    DateTime OccurredAt) : IDomainEvent;
