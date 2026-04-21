using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Auth.Configuration;

namespace Skinora.Auth.Application.SteamAuthentication;

public sealed class SteamProfileClient : ISteamProfileClient
{
    private const string Endpoint = "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/";

    private readonly HttpClient _httpClient;
    private readonly SteamOpenIdSettings _settings;
    private readonly ILogger<SteamProfileClient> _logger;

    public SteamProfileClient(
        HttpClient httpClient,
        IOptions<SteamOpenIdSettings> settings,
        ILogger<SteamProfileClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SteamPlayerSummary?> GetPlayerSummaryAsync(
        string steamId64, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.WebApiKey))
        {
            _logger.LogWarning(
                "Steam Web API key is not configured — skipping GetPlayerSummaries for {SteamId}",
                steamId64);
            return null;
        }

        var url = $"{Endpoint}?steamids={Uri.EscapeDataString(steamId64)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-webapi-key", _settings.WebApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetPlayerSummaries returned non-success status {StatusCode} for {SteamId}",
                (int)response.StatusCode, steamId64);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("response", out var envelope) ||
            !envelope.TryGetProperty("players", out var players) ||
            players.ValueKind != JsonValueKind.Array ||
            players.GetArrayLength() == 0)
        {
            return null;
        }

        var first = players[0];
        var steamId = first.TryGetProperty("steamid", out var sid) ? sid.GetString() : null;
        var persona = first.TryGetProperty("personaname", out var pn) ? pn.GetString() : null;
        var avatar = first.TryGetProperty("avatarfull", out var af) ? af.GetString() : null;

        if (steamId is null || persona is null)
            return null;

        DateTime? createdAt = null;
        if (first.TryGetProperty("timecreated", out var tc) &&
            tc.ValueKind == JsonValueKind.Number &&
            tc.TryGetInt64(out var epochSeconds) &&
            epochSeconds > 0)
        {
            createdAt = DateTimeOffset.FromUnixTimeSeconds(epochSeconds).UtcDateTime;
        }

        return new SteamPlayerSummary(steamId, persona, avatar, createdAt);
    }
}
