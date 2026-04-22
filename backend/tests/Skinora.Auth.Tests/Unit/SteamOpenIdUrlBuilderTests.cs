using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Configuration;

namespace Skinora.Auth.Tests.Unit;

public class SteamOpenIdUrlBuilderTests
{
    private static SteamOpenIdSettings Settings() => new()
    {
        Realm = "https://skinora.test",
        ReturnToUrl = "https://skinora.test/api/v1/auth/steam/callback",
        ReVerifyReturnToUrl = "https://skinora.test/api/v1/auth/steam/re-verify/callback",
        FrontendCallbackUrl = "https://skinora.test/auth/callback",
    };

    [Fact]
    public void Build_StartsWithSteamOpenIdEndpoint()
    {
        var url = SteamOpenIdUrlBuilder.Build(Settings());

        Assert.StartsWith("https://steamcommunity.com/openid/login?", url);
    }

    [Fact]
    public void Build_IncludesRequiredOpenIdParameters()
    {
        var url = SteamOpenIdUrlBuilder.Build(Settings());

        Assert.Contains("openid.ns=" + Uri.EscapeDataString("http://specs.openid.net/auth/2.0"), url);
        Assert.Contains("openid.mode=checkid_setup", url);
        Assert.Contains("openid.realm=" + Uri.EscapeDataString("https://skinora.test"), url);
        Assert.Contains("openid.return_to=" +
            Uri.EscapeDataString("https://skinora.test/api/v1/auth/steam/callback"), url);
        Assert.Contains("openid.identity=" +
            Uri.EscapeDataString("http://specs.openid.net/auth/2.0/identifier_select"), url);
        Assert.Contains("openid.claimed_id=" +
            Uri.EscapeDataString("http://specs.openid.net/auth/2.0/identifier_select"), url);
    }
}
