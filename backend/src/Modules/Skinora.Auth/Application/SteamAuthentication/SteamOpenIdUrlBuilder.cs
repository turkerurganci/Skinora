using Skinora.Auth.Configuration;

namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Builds the Steam OpenID 2.0 login redirect URL — 08 §2.1.
/// </summary>
public static class SteamOpenIdUrlBuilder
{
    public const string LoginEndpoint = "https://steamcommunity.com/openid/login";
    private const string Ns = "http://specs.openid.net/auth/2.0";
    private const string IdentifierSelect = "http://specs.openid.net/auth/2.0/identifier_select";

    public static string Build(SteamOpenIdSettings settings)
    {
        var query = new Dictionary<string, string>
        {
            ["openid.ns"] = Ns,
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = settings.ReturnToUrl,
            ["openid.realm"] = settings.Realm,
            ["openid.identity"] = IdentifierSelect,
            ["openid.claimed_id"] = IdentifierSelect,
        };

        var qs = string.Join("&", query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return $"{LoginEndpoint}?{qs}";
    }
}
