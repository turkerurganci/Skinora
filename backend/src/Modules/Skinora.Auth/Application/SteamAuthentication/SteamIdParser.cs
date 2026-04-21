namespace Skinora.Auth.Application.SteamAuthentication;

/// <summary>
/// Extracts SteamID64 from the OpenID <c>claimed_id</c> URL per 08 §2.1.
/// Expected format: <c>https://steamcommunity.com/openid/id/{steamId64}</c>.
/// </summary>
public static class SteamIdParser
{
    private const string Prefix = "https://steamcommunity.com/openid/id/";

    public static bool TryParse(string? claimedId, out string steamId64)
    {
        steamId64 = string.Empty;

        if (string.IsNullOrWhiteSpace(claimedId))
            return false;

        if (!claimedId.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var candidate = claimedId[Prefix.Length..].TrimEnd('/');

        if (candidate.Length is < 15 or > 20)
            return false;

        foreach (var c in candidate)
        {
            if (c is < '0' or > '9')
                return false;
        }

        steamId64 = candidate;
        return true;
    }
}
