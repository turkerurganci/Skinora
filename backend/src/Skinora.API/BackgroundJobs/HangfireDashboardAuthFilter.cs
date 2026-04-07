using System.Linq;
using Hangfire.Dashboard;
using Skinora.Auth.Configuration;

namespace Skinora.API.BackgroundJobs;

/// <summary>
/// Restricts the Hangfire dashboard to authenticated administrators (T09 kabul
/// kriteri "Hangfire dashboard admin auth arkasında mı?").
/// </summary>
/// <remarks>
/// The filter checks the current <c>HttpContext</c>'s authenticated principal
/// for an <see cref="AuthClaimTypes.Role"/> claim equal to
/// <see cref="AuthRoles.Admin"/> or <see cref="AuthRoles.SuperAdmin"/>.
/// Anonymous users and authenticated non-admins receive a 401 from Hangfire's
/// pipeline. The same role gating is used by the API's
/// <c>AuthPolicies.AdminAccess</c> policy (T06).
/// </remarks>
public sealed class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var user = httpContext.User;

        if (user?.Identity is null || !user.Identity.IsAuthenticated)
        {
            return false;
        }

        return user.Claims.Any(c =>
            c.Type == AuthClaimTypes.Role &&
            (c.Value == AuthRoles.Admin || c.Value == AuthRoles.SuperAdmin));
    }
}
