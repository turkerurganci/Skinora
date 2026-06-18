using System.Security.Claims;
using Skinora.Auth.Configuration;

namespace Skinora.Realtime.Hubs;

/// <summary>
/// Shared claim helpers for the SignalR hubs (WP9). Admin identity is derived
/// from the JWT <c>role</c> claim — <see cref="AccessTokenGenerator"/> emits one
/// of <see cref="AuthRoles.User"/> / <see cref="AuthRoles.Admin"/> /
/// <see cref="AuthRoles.SuperAdmin"/> — read directly (not via
/// <c>ClaimsPrincipal.IsInRole</c>) so the result is independent of the bearer
/// pipeline's role-claim mapping.
/// </summary>
public static class HubClaims
{
    /// <summary>
    /// True when the connection carries an admin role claim (<c>admin</c> or
    /// <c>super_admin</c>). Used to scope admin-only pushes to the
    /// <c>NotificationsHub</c> admin group (T69 K4) and to bypass the
    /// per-transaction membership check on <c>TransactionsHub</c> (T61 K3).
    /// </summary>
    public static bool IsAdmin(ClaimsPrincipal? user)
    {
        var role = user?.FindFirstValue(AuthClaimTypes.Role);
        return role is AuthRoles.Admin or AuthRoles.SuperAdmin;
    }
}
