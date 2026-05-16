using Skinora.Steam.Domain.Entities;

namespace Skinora.Steam.Application.BotSelection;

/// <summary>
/// Capacity-based platform Steam bot selection (T69 — 02 §15, 05 §3.2, 06 §3.10).
/// Returns the ACTIVE bot currently holding the fewest escrowed items so the
/// pool stays balanced across all trade-offer dispatches.
/// </summary>
/// <remarks>
/// <para>
/// The dispatch caller (forward-deferred — see <c>TXX_REPORT</c> Known
/// Limitations) consumes this service and forwards the resolved bot's
/// <see cref="PlatformSteamBot.DisplayName"/> as the sidecar's
/// <c>botAccountName</c>. Without a caller wired today the service is
/// reachable via unit tests and via future task callers; nothing in the
/// production path invokes it yet.
/// </para>
/// <para>
/// Selection rules:
/// </para>
/// <list type="bullet">
///   <item><c>Status == ACTIVE</c> (RESTRICTED / BANNED / OFFLINE filtered out)</item>
///   <item><c>IsDeleted == false</c> (soft-deleted rows ineligible)</item>
///   <item>Order by <c>ActiveEscrowCount</c> ascending (denormalized counter — 06 §3.10)</item>
///   <item>Tie-break: oldest <c>LastHealthCheckAt</c> first (least recently used → distribute load
///         across freshly-probed bots), then <c>Id</c> for deterministic ordering</item>
/// </list>
/// </remarks>
public interface IBotSelectionService
{
    /// <summary>
    /// Resolve the bot that should receive the next trade-offer dispatch.
    /// Returns <c>null</c> when no ACTIVE bot is available — the caller must
    /// surface this as a transient "no capacity" failure (admin alert path
    /// covered by <see cref="ISteamWebhookHandler"/> bot lifecycle events).
    /// </summary>
    Task<PlatformSteamBot?> SelectAsync(CancellationToken cancellationToken);
}
