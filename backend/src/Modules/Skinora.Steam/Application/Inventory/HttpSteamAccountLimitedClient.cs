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
/// <see cref="ISteamAccountLimitedProbe"/> (08 §2.2a). Calls the Steam sidecar's
/// <c>GET /api/account-limited/{steamId}</c> endpoint, which reads the
/// anonymous Steam Community profile document and confirms a negative answer
/// over several consecutive samples before returning it.
/// </summary>
/// <remarks>
/// Fails closed: any transport / upstream / parse failure maps to
/// <see cref="SteamAccountLimitedProbeResult.Unavailable"/>, which the gates
/// translate into a TRANSIENT "could not check" error rather than a permanent
/// rejection — the repo rule that absence of information and a negative finding
/// never collapse onto one code.
///
/// <para>
/// The <c>limited</c> field is deserialized as <c>bool?</c> ON PURPOSE. With a
/// plain <c>bool</c> a payload that lacks the field would bind to <c>false</c>
/// and this client would report "not limited" for a body it never understood —
/// the same fail-open shape the sidecar-side parser was written to avoid, one
/// layer up.
/// </para>
/// </remarks>
public sealed class HttpSteamAccountLimitedClient : ISteamAccountLimitedProbe
{
    /// <summary>HTTP client name used by <c>AddHttpClient</c>.</summary>
    public const string HttpClientName = "SteamSidecarAccountLimited";

    /// <summary>Service-to-service auth header (05 §3.4).</summary>
    private const string InternalKeyHeader = "X-Internal-Key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly SteamSidecarOptions _options;
    private readonly ILogger<HttpSteamAccountLimitedClient> _logger;

    public HttpSteamAccountLimitedClient(
        HttpClient http,
        IOptions<SteamSidecarOptions> options,
        ILogger<HttpSteamAccountLimitedClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SteamAccountLimitedProbeResult> ProbeAsync(
        string steamId64,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(steamId64))
        {
            // No usable input — fail closed without a round-trip.
            return SteamAccountLimitedProbeResult.Unavailable;
        }

        using var request = BuildRequest(HttpMethod.Get, $"api/account-limited/{steamId64}");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Steam sidecar limited-account request failed for {SteamId}", steamId64);
            return SteamAccountLimitedProbeResult.Unavailable;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 503 STEAM_PROFILE_UNREADABLE (the sidecar's own fail-closed
                // branch) and any other non-2xx → unavailable.
                _logger.LogWarning(
                    "Steam sidecar limited-account returned {StatusCode} for {SteamId}",
                    (int)response.StatusCode, steamId64);
                return SteamAccountLimitedProbeResult.Unavailable;
            }

            SidecarAccountLimitedEnvelope? payload;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<SidecarAccountLimitedEnvelope>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Steam sidecar limited-account payload could not be parsed for {SteamId}", steamId64);
                return SteamAccountLimitedProbeResult.Unavailable;
            }

            if (payload?.Limited is not { } limited)
            {
                _logger.LogWarning(
                    "Steam sidecar limited-account returned no usable 'limited' field for {SteamId}",
                    steamId64);
                return SteamAccountLimitedProbeResult.Unavailable;
            }

            return limited
                ? SteamAccountLimitedProbeResult.Limited
                : SteamAccountLimitedProbeResult.Clear;
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

    private sealed record SidecarAccountLimitedEnvelope(
        [property: JsonPropertyName("limited")] bool? Limited,
        [property: JsonPropertyName("samples")] int Samples,
        [property: JsonPropertyName("source")] string? Source);
}
