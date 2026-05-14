using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Transactions.Application.Steam;

namespace Skinora.Steam.Application.Inventory;

/// <summary>
/// <see cref="HttpClient"/>-backed implementation of
/// <see cref="ISteamSidecarInventoryClient"/> and the
/// <see cref="ISteamInventoryCacheInvalidator"/> port consumed by
/// <c>TransactionCreationService</c>. Both ports route to the same sidecar
/// container so they share a single typed client registration.
/// </summary>
/// <remarks>
/// JSON shape: the sidecar emits camelCase. <see cref="JsonSerializerOptions"/>
/// is constructed once per request via the default web defaults (handled by
/// <see cref="HttpClientJsonExtensions.ReadFromJsonAsync{TValue}(HttpContent,JsonSerializerOptions?,CancellationToken)"/>
/// when no explicit options are supplied) — the typed records below pin
/// each field name with <see cref="JsonPropertyNameAttribute"/> so naming
/// stays correct even if the global policy drifts.
/// </remarks>
public sealed class HttpSteamSidecarInventoryClient
    : ISteamSidecarInventoryClient, ISteamInventoryCacheInvalidator
{
    /// <summary>HTTP client name used by <c>AddHttpClient</c>.</summary>
    public const string HttpClientName = "SteamSidecar";

    /// <summary>Service-to-service auth header (05 §3.4).</summary>
    private const string InternalKeyHeader = "X-Internal-Key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly SteamSidecarOptions _options;
    private readonly ILogger<HttpSteamSidecarInventoryClient> _logger;

    public HttpSteamSidecarInventoryClient(
        HttpClient http,
        IOptions<SteamSidecarOptions> options,
        ILogger<HttpSteamSidecarInventoryClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SteamSidecarInventoryResult> GetInventoryAsync(
        string steamId, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Get, $"api/inventory/{steamId}");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Steam sidecar inventory request failed for {SteamId}", steamId);
            return new SteamSidecarInventoryResult(SteamSidecarStatus.Unavailable, Inventory: null);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                _logger.LogInformation("Steam inventory for {SteamId} is private", steamId);
                return new SteamSidecarInventoryResult(SteamSidecarStatus.InventoryPrivate, Inventory: null);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Steam sidecar inventory returned {StatusCode} for {SteamId}",
                    (int)response.StatusCode, steamId);
                return new SteamSidecarInventoryResult(SteamSidecarStatus.Unavailable, Inventory: null);
            }

            SidecarInventoryEnvelope? payload;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<SidecarInventoryEnvelope>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Steam sidecar inventory payload could not be parsed for {SteamId}", steamId);
                return new SteamSidecarInventoryResult(SteamSidecarStatus.Unavailable, Inventory: null);
            }

            if (payload is null)
            {
                _logger.LogWarning("Steam sidecar inventory returned empty body for {SteamId}", steamId);
                return new SteamSidecarInventoryResult(SteamSidecarStatus.Unavailable, Inventory: null);
            }

            return new SteamSidecarInventoryResult(SteamSidecarStatus.Success, payload.ToDto());
        }
    }

    public async Task<SteamSidecarStatus> InvalidateInventoryAsync(
        string steamId, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Delete, $"api/inventory/{steamId}/cache");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return SteamSidecarStatus.Success;

            _logger.LogWarning(
                "Steam sidecar cache invalidation returned {StatusCode} for {SteamId}",
                (int)response.StatusCode, steamId);
            return SteamSidecarStatus.Unavailable;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Steam sidecar cache invalidation failed for {SteamId}", steamId);
            return SteamSidecarStatus.Unavailable;
        }
    }

    Task ISteamInventoryCacheInvalidator.InvalidateAsync(string steamId, CancellationToken cancellationToken)
        => InvalidateInventoryAsync(steamId, cancellationToken);

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

    private sealed record SidecarInventoryEnvelope(
        [property: JsonPropertyName("items")] IReadOnlyList<SidecarInventoryItem> Items,
        [property: JsonPropertyName("totalCount")] int TotalCount,
        [property: JsonPropertyName("tradeableCount")] int TradeableCount)
    {
        public SteamInventoryDto ToDto() => new(
            Items: Items.Select(it => it.ToDto()).ToList(),
            TotalCount: TotalCount,
            TradeableCount: TradeableCount);
    }

    private sealed record SidecarInventoryItem(
        [property: JsonPropertyName("assetId")] string AssetId,
        [property: JsonPropertyName("classId")] string ClassId,
        [property: JsonPropertyName("instanceId")] string? InstanceId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("marketHashName")] string MarketHashName,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("exterior")] string? Exterior,
        [property: JsonPropertyName("iconUrl")] string? IconUrl,
        [property: JsonPropertyName("tradable")] bool Tradable,
        [property: JsonPropertyName("marketable")] bool Marketable)
    {
        public SteamInventoryItemDto ToDto() => new(
            AssetId: AssetId,
            ClassId: ClassId,
            InstanceId: InstanceId,
            Name: Name,
            MarketHashName: MarketHashName,
            Type: Type,
            Wear: Exterior,
            ImageUrl: IconUrl,
            Tradeable: Tradable,
            Marketable: Marketable);
    }
}
