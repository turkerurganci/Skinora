namespace Skinora.Users.Application.Settings;

/// <summary>
/// Parses a Steam trade URL into its <c>partner</c> (SteamID32, integer) and
/// <c>token</c> query parameters (07 §5.16a). Returns <c>null</c> when the
/// URL is malformed, has the wrong host, or is missing one of the required
/// query values. The real URL format is
/// <c>https://steamcommunity.com/tradeoffer/new/?partner=...&amp;token=...</c>.
/// </summary>
public interface ITradeUrlParser
{
    TradeUrlComponents? Parse(string? tradeUrl);
}

public sealed record TradeUrlComponents(string Normalized, string Partner, string Token);

public sealed class TradeUrlParser : ITradeUrlParser
{
    private const string ExpectedHost = "steamcommunity.com";
    private const string ExpectedPath = "/tradeoffer/new/";

    public TradeUrlComponents? Parse(string? tradeUrl)
    {
        if (string.IsNullOrWhiteSpace(tradeUrl)) return null;
        if (!Uri.TryCreate(tradeUrl.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
        if (!string.Equals(uri.Host, ExpectedHost, StringComparison.OrdinalIgnoreCase)) return null;
        if (!string.Equals(uri.AbsolutePath, ExpectedPath, StringComparison.Ordinal)) return null;

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var partner = query.Get("partner");
        var token = query.Get("token");
        if (string.IsNullOrWhiteSpace(partner) || string.IsNullOrWhiteSpace(token))
            return null;
        if (!partner.All(char.IsDigit)) return null;
        if (!token.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')) return null;
        if (partner.Length > 20 || token.Length > 20) return null;

        var normalized = $"https://{ExpectedHost}{ExpectedPath}?partner={partner}&token={token}";
        return new TradeUrlComponents(normalized, partner, token);
    }
}
