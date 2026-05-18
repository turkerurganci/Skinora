using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skinora.Shared.Discord;

/// <summary>
/// <see cref="IDiscordOAuthClient"/> backed by a plain
/// <see cref="HttpClient"/> against the Discord API v10 OAuth2 + Users
/// endpoints. Plan T80 deliberately rules the Discord.Net NuGet out —
/// a hand-rolled wrapper keeps the dependency surface minimal, matches
/// the <see cref="Email.ResendEmailClient"/> + Telegram precedent and
/// lets us inject a mock <see cref="HttpMessageHandler"/> in unit
/// tests.
/// </summary>
/// <remarks>
/// <para>
/// Error mapping per 08 §6.4 OAuth2 hata tablosu:
/// </para>
/// <list type="bullet">
///   <item>200 + body parses → <see cref="DiscordProfile"/>.</item>
///   <item>4xx with <c>invalid_grant</c> →
///         <see cref="DiscordOAuthExchangeException"/>
///         (<see cref="DiscordOAuthFailureReason.InvalidGrant"/>).</item>
///   <item>5xx / transport failure / token-exchange-not-OK →
///         <see cref="DiscordOAuthExchangeException"/>
///         (<see cref="DiscordOAuthFailureReason.TokenExchangeFailed"/>).</item>
///   <item><c>/users/@me</c> non-OK after a successful token exchange →
///         <see cref="DiscordOAuthExchangeException"/>
///         (<see cref="DiscordOAuthFailureReason.UsersMeFailed"/>).</item>
/// </list>
/// </remarks>
public sealed class DiscordOAuthClient : IDiscordOAuthClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _httpClient;
    private readonly DiscordSettings _settings;
    private readonly ILogger<DiscordOAuthClient> _logger;

    public DiscordOAuthClient(
        HttpClient httpClient,
        IOptions<DiscordSettings> settings,
        ILogger<DiscordOAuthClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.ClientId)
            || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new InvalidOperationException(
                "Discord OAuth client requires Discord:ClientId and Discord:ClientSecret. " +
                "Set Discord:Provider to 'logging' for non-production environments.");
        }

        if (_httpClient.BaseAddress is null)
        {
            var baseUrl = _settings.BaseUrl.TrimEnd('/');
            _httpClient.BaseAddress = new Uri(baseUrl + "/", UriKind.Absolute);
        }
    }

    public async Task<DiscordProfile?> ExchangeAsync(
        string authorizationCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authorizationCode))
        {
            return null;
        }

        var accessToken = await ExchangeCodeForAccessTokenAsync(
            authorizationCode, cancellationToken);

        return await FetchProfileAsync(accessToken, cancellationToken);
    }

    private async Task<string> ExchangeCodeForAccessTokenAsync(
        string code, CancellationToken cancellationToken)
    {
        var formFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = _settings.ClientId,
            ["client_secret"] = _settings.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _settings.RedirectUri,
        };

        HttpResponseMessage response;
        try
        {
            using var form = new FormUrlEncodedContent(formFields);
            response = await _httpClient.PostAsync("oauth2/token", form, cancellationToken);
        }
        catch (TaskCanceledException tex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DiscordOAuthExchangeException(
                DiscordOAuthFailureReason.TokenExchangeFailed,
                "Discord OAuth2 token exchange timed out.",
                innerException: tex);
        }
        catch (HttpRequestException hex)
        {
            throw new DiscordOAuthExchangeException(
                DiscordOAuthFailureReason.TokenExchangeFailed,
                $"Discord OAuth2 token exchange transport failure: {hex.Message}",
                innerException: hex);
        }

        var statusCode = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            string? errorBody = null;
            try
            {
                errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception readEx)
            {
                _logger.LogWarning(
                    readEx,
                    "Discord OAuth2 token exchange — failed to read non-success body");
            }

            var reason = LooksLikeInvalidGrant(statusCode, errorBody)
                ? DiscordOAuthFailureReason.InvalidGrant
                : DiscordOAuthFailureReason.TokenExchangeFailed;

            _logger.LogWarning(
                "Discord OAuth2 token exchange failed — status={Status} reason={Reason}",
                statusCode,
                reason);

            throw new DiscordOAuthExchangeException(
                reason,
                $"Discord OAuth2 token exchange returned HTTP {statusCode}.",
                httpStatusCode: statusCode);
        }

        TokenResponse? token;
        try
        {
            token = await response.Content
                .ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken);
        }
        catch (JsonException jex)
        {
            throw new DiscordOAuthExchangeException(
                DiscordOAuthFailureReason.TokenExchangeFailed,
                "Discord OAuth2 token exchange returned 200 but the body could not be parsed.",
                httpStatusCode: statusCode,
                innerException: jex);
        }

        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new DiscordOAuthExchangeException(
                DiscordOAuthFailureReason.TokenExchangeFailed,
                "Discord OAuth2 token exchange returned 200 with no access_token.",
                httpStatusCode: statusCode);
        }

        return token.AccessToken;
    }

    private async Task<DiscordProfile?> FetchProfileAsync(
        string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "users/@me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException tex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DiscordOAuthExchangeException(
                DiscordOAuthFailureReason.UsersMeFailed,
                "Discord /users/@me timed out.",
                innerException: tex);
        }
        catch (HttpRequestException hex)
        {
            throw new DiscordOAuthExchangeException(
                DiscordOAuthFailureReason.UsersMeFailed,
                $"Discord /users/@me transport failure: {hex.Message}",
                innerException: hex);
        }

        var statusCode = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Discord /users/@me failed — status={Status}", statusCode);

            throw new DiscordOAuthExchangeException(
                DiscordOAuthFailureReason.UsersMeFailed,
                $"Discord /users/@me returned HTTP {statusCode}.",
                httpStatusCode: statusCode);
        }

        UsersMeResponse? body;
        try
        {
            body = await response.Content
                .ReadFromJsonAsync<UsersMeResponse>(JsonOptions, cancellationToken);
        }
        catch (JsonException jex)
        {
            throw new DiscordOAuthExchangeException(
                DiscordOAuthFailureReason.UsersMeFailed,
                "Discord /users/@me returned 200 but the body could not be parsed.",
                httpStatusCode: statusCode,
                innerException: jex);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Id))
        {
            return null;
        }

        // Discord deprecated the legacy "username#discriminator" handle
        // in 2023; new accounts have discriminator "0" and global_name.
        // We surface global_name when present (matches what the user
        // sees in their Discord client) and fall back to username
        // otherwise. The username field is always populated.
        var displayName = !string.IsNullOrWhiteSpace(body.GlobalName)
            ? body.GlobalName!
            : body.Username ?? string.Empty;

        return new DiscordProfile(body.Id, displayName);
    }

    private static bool LooksLikeInvalidGrant(int statusCode, string? body)
    {
        if (statusCode < 400 || statusCode >= 500)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            // Treat any 4xx without a parseable body as invalid_grant —
            // a 400/401 against the token endpoint with no detail almost
            // always means a stale / replayed code.
            return true;
        }

        return body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }

        public string? Scope { get; set; }
    }

    private sealed class UsersMeResponse
    {
        public string? Id { get; set; }
        public string? Username { get; set; }

        [JsonPropertyName("global_name")]
        public string? GlobalName { get; set; }
    }
}
