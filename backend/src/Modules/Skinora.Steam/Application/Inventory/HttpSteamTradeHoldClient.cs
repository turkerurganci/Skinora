using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Shared.Steam;

namespace Skinora.Steam.Application.Inventory;

/// <summary>
/// <see cref="HttpClient"/>-backed implementation of
/// <see cref="ISteamTradeHoldProbe"/> (WP6). Calls the Steam sidecar's
/// <c>GET /api/trade-hold/{steamId}</c> endpoint (08 §2.2 →
/// <c>IEconService/GetTradeHoldDurations/v1</c>). Shares
/// <see cref="SteamSidecarOptions"/> with the inventory client — same sidecar
/// container, same <c>X-Internal-Key</c> auth — but is its own typed client so
/// the trade-hold timeout stays independent of inventory pagination.
/// </summary>
/// <remarks>
/// Fails closed: any transport / upstream / parse failure maps to
/// <see cref="SteamTradeHoldProbeResult.Unavailable"/> (Available=false). The
/// checkers translate that into the 07 §5.16a <c>STEAM_API_UNAVAILABLE</c>
/// fallback that blocks transaction start — never a silent "MA active".
/// </remarks>
public sealed class HttpSteamTradeHoldClient : ISteamTradeHoldProbe
{
    /// <summary>HTTP client name used by <c>AddHttpClient</c>.</summary>
    public const string HttpClientName = "SteamSidecarTradeHold";

    /// <summary>Service-to-service auth header (05 §3.4).</summary>
    private const string InternalKeyHeader = "X-Internal-Key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly SteamSidecarOptions _options;
    private readonly ILogger<HttpSteamTradeHoldClient> _logger;

    public HttpSteamTradeHoldClient(
        HttpClient http,
        IOptions<SteamSidecarOptions> options,
        ILogger<HttpSteamTradeHoldClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SteamTradeHoldProbeResult> ProbeAsync(
        string steamId64,
        string tradeOfferAccessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(steamId64) || string.IsNullOrWhiteSpace(tradeOfferAccessToken))
        {
            // No usable inputs — fail closed without a round-trip.
            return SteamTradeHoldProbeResult.Unavailable;
        }

        var relativeUri =
            $"api/trade-hold/{steamId64}?accessToken={Uri.EscapeDataString(tradeOfferAccessToken)}";
        using var request = BuildRequest(HttpMethod.Get, relativeUri);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Transport (HttpRequestException), timeout (TaskCanceledException) and
            // configuration failures — e.g. an unset BaseUrl turning the relative
            // URI invalid (InvalidOperationException) — all fail closed per the
            // ISteamTradeHoldProbe contract.
            _logger.LogWarning(ex, "Steam sidecar trade-hold request failed for {SteamId}", steamId64);
            return SteamTradeHoldProbeResult.Unavailable;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 503 (key missing / Steam upstream) and any other non-2xx →
                // unavailable. The sidecar already logged the upstream detail.
                _logger.LogWarning(
                    "Steam sidecar trade-hold returned {StatusCode} for {SteamId}",
                    (int)response.StatusCode, steamId64);
                return SteamTradeHoldProbeResult.Unavailable;
            }

            SidecarTradeHoldEnvelope? payload;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<SidecarTradeHoldEnvelope>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Steam sidecar trade-hold payload could not be parsed for {SteamId}", steamId64);
                return SteamTradeHoldProbeResult.Unavailable;
            }

            if (payload is null)
            {
                _logger.LogWarning("Steam sidecar trade-hold returned empty body for {SteamId}", steamId64);
                return SteamTradeHoldProbeResult.Unavailable;
            }

            return payload.Active
                ? SteamTradeHoldProbeResult.Active
                : SteamTradeHoldProbeResult.Inactive;
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string relativeUri)
    {
        var request = new HttpRequestMessage(method, relativeUri);
        if (!string.IsNullOrEmpty(_options.InternalKey))
        {
            request.Headers.TryAddWithoutValidation(InternalKeyHeader, _options.InternalKey);
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private sealed record SidecarTradeHoldEnvelope(
        [property: JsonPropertyName("active")] bool Active,
        [property: JsonPropertyName("escrowEndDurationSeconds")] long EscrowEndDurationSeconds);
}
