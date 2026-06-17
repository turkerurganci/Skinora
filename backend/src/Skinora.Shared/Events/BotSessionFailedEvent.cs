using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the Steam webhook handler (WP8 — 02 §15, 05 §3.2, 08 §3.3) on
/// every non-idempotent platform-bot lifecycle transition INTO a non-ACTIVE
/// status (OFFLINE / RESTRICTED / BANNED) — the sidecar
/// <c>bot.session_failed</c> / <c>bot.removed_from_pool</c> events never map a
/// bot back to ACTIVE, so each processed transition is a fresh incident
/// (including degraded-to-degraded hops such as RESTRICTED → OFFLINE). The
/// Notifications consumer fans out an
/// <see cref="Skinora.Shared.Enums.NotificationType.ADMIN_STEAM_BOT_ISSUE"/>
/// in-app notification to every admin so a degraded platform Steam account is
/// visible on the admin inbox — not only in Loki logs and the transient
/// realtime banner.
/// </summary>
/// <remarks>
/// Distinct from <see cref="BotRestrictedEvent"/>: that event fires only for
/// the durable RESTRICTED / BANNED subset and drives the recovery queue. This
/// event fires for the full lifecycle-incident surface (including transient
/// OFFLINE session failures) and exists solely to raise the admin alert. Both
/// may fire for the same RESTRICTED / BANNED transition — they serve different
/// consumers and are independently idempotent on the outbox.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="PlatformSteamBotId">Bot whose session / pool membership failed.</param>
/// <param name="SteamId">Bot Steam ID (snapshot for logging / audit).</param>
/// <param name="DisplayName">Bot display name (snapshot).</param>
/// <param name="PreviousStatus">Bot status before the transition.</param>
/// <param name="NewStatus">Bot status after the transition (OFFLINE / RESTRICTED / BANNED).</param>
/// <param name="Reason">Sidecar failure reason from the webhook payload.</param>
/// <param name="WebhookEvent">Sidecar event name (<c>bot.session_failed</c> / <c>bot.removed_from_pool</c>).</param>
/// <param name="OccurredAt">UTC timestamp the transition was committed.</param>
public record BotSessionFailedEvent(
    Guid EventId,
    Guid PlatformSteamBotId,
    string SteamId,
    string DisplayName,
    string PreviousStatus,
    string NewStatus,
    string? Reason,
    string WebhookEvent,
    DateTime OccurredAt) : IDomainEvent;
