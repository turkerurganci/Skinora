using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Skinora.Auth.Configuration;

namespace Skinora.Realtime.Hubs;

/// <summary>
/// SignalR hub serving the user-scoped real-time notification channel
/// (T62 — 07 §11.2 RT2, mounted at <c>/hubs/notifications</c>).
/// </summary>
/// <remarks>
/// <para>
/// Authentication is enforced by the framework <see cref="AuthorizeAttribute"/>;
/// the JWT bearer pipeline (<c>AuthModule.OnMessageReceived</c>) accepts the
/// token from a <c>?access_token=</c> query parameter on hub connection
/// requests so the SignalR JS client can connect through WebSockets without
/// custom headers (07 §11.2 "Auth: JWT query param").
/// </para>
/// <para>
/// Group naming uses <see cref="GroupName(Guid)"/>. Unlike the transactions hub
/// (T61 — RT1) which uses per-transaction rooms shared by buyer + seller,
/// notifications are strictly per-user, so connections are auto-joined to a
/// <c>user:{userId:N}</c> group on <see cref="OnConnectedAsync"/>. The hub
/// exposes no client→server methods because the spec table for RT2 lists none —
/// the frontend connects on login (07 §11.2 "Bağlantı") and listens for
/// <c>NewNotification</c>, <c>UnreadCountChanged</c>, <c>TelegramConnected</c>,
/// <c>DiscordConnected</c> and <c>MaintenanceStatusChanged</c> server pushes.
/// </para>
/// <para>
/// A single user may hold multiple connections (multiple browser tabs / mobile
/// + desktop). SignalR's group dispatch fans the push out to every connection
/// in the group automatically.
/// </para>
/// </remarks>
[Authorize]
public class NotificationsHub : Hub
{
    public const string GroupPrefix = "user:";

    /// <summary>
    /// Group that receives the admin-scoped pushes (T69 K4 — bot status,
    /// reconciliation mismatch, hot-wallet threshold). Admins are auto-joined on
    /// <see cref="OnConnectedAsync"/>; non-admin connections never join, so these
    /// payloads (bot SteamIds, wallet balances, reconciliation deltas) no longer
    /// fan out to every client via <c>Clients.All</c>.
    /// </summary>
    public const string AdminGroup = "admins";

    private readonly ILogger<NotificationsHub> _logger;

    public NotificationsHub(ILogger<NotificationsHub> logger)
    {
        _logger = logger;
    }

    /// <summary>Stable group name for a given user id.</summary>
    public static string GroupName(Guid userId) => $"{GroupPrefix}{userId:N}";

    public override async Task OnConnectedAsync()
    {
        var userId = ResolveUserId();

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(userId), Context.ConnectionAborted);

        // WP9 — admins also join the admin broadcast group so the three
        // admin-scoped events reach them without leaking to non-admin clients.
        if (HubClaims.IsAdmin(Context.User))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup, Context.ConnectionAborted);
            _logger.LogDebug(
                "NotificationsHub connection {ConnectionId} joined the admin group (user {UserId}).",
                Context.ConnectionId, userId);
        }

        _logger.LogDebug(
            "NotificationsHub connection {ConnectionId} joined group for user {UserId}.",
            Context.ConnectionId, userId);

        await base.OnConnectedAsync();
    }

    private Guid ResolveUserId()
    {
        var raw = Context.User?.FindFirstValue(AuthClaimTypes.UserId);
        if (!Guid.TryParse(raw, out var userId))
        {
            throw new HubException("AUTH_INVALID");
        }
        return userId;
    }
}
