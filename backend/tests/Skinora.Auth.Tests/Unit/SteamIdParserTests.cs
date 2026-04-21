using Skinora.Auth.Application.SteamAuthentication;

namespace Skinora.Auth.Tests.Unit;

public class SteamIdParserTests
{
    [Theory]
    [InlineData("https://steamcommunity.com/openid/id/76561198012345678", "76561198012345678")]
    [InlineData("https://steamcommunity.com/openid/id/76561198012345678/", "76561198012345678")]
    public void TryParse_ValidUrl_ReturnsSteamId64(string claimedId, string expected)
    {
        var ok = SteamIdParser.TryParse(claimedId, out var steamId);

        Assert.True(ok);
        Assert.Equal(expected, steamId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://steamcommunity.com/openid/id/76561198012345678")]   // http, not https
    [InlineData("https://steamcommunity.com/openid/76561198012345678")]      // missing /id/
    [InlineData("https://evil.com/openid/id/76561198012345678")]             // wrong host
    [InlineData("https://steamcommunity.com/openid/id/abcdefghijklmnop")]    // non-digit
    [InlineData("https://steamcommunity.com/openid/id/123")]                 // too short
    [InlineData("https://steamcommunity.com/openid/id/123456789012345678901")] // too long
    public void TryParse_InvalidUrl_ReturnsFalse(string? claimedId)
    {
        var ok = SteamIdParser.TryParse(claimedId, out var steamId);

        Assert.False(ok);
        Assert.Equal(string.Empty, steamId);
    }
}
