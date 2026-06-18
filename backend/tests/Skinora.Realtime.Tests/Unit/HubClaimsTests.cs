using System.Security.Claims;
using Skinora.Auth.Configuration;
using Skinora.Realtime.Hubs;

namespace Skinora.Realtime.Tests.Unit;

/// <summary>
/// WP9 — the admin role gate that backs both the TransactionsHub join bypass
/// (T61 K3) and the NotificationsHub admin group scope (T69 K4).
/// </summary>
public class HubClaimsTests
{
    [Theory]
    [InlineData(AuthRoles.Admin, true)]
    [InlineData(AuthRoles.SuperAdmin, true)]
    [InlineData(AuthRoles.User, false)]
    [InlineData("something-else", false)]
    public void IsAdmin_TracksRoleClaim(string role, bool expected)
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(AuthClaimTypes.Role, role)]));

        Assert.Equal(expected, HubClaims.IsAdmin(principal));
    }

    [Fact]
    public void IsAdmin_NullPrincipal_IsFalse() =>
        Assert.False(HubClaims.IsAdmin(null));

    [Fact]
    public void IsAdmin_NoRoleClaim_IsFalse() =>
        Assert.False(HubClaims.IsAdmin(new ClaimsPrincipal(new ClaimsIdentity())));
}
