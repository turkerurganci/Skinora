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
        string steamId, bool bypassCache, CancellationToken cancellationToken)
    {
        // T123 — 08 §2.3 cache bypass. The flag is omitted rather than sent as
        // `refresh=false` on the cached path: absent means "use the cache" in
        // the sidecar's own parser (routes.ts parseRefreshParam), so the
        // ordinary read keeps its existing wire shape byte for byte.
        var path = bypassCache
            ? $"api/inventory/{steamId}?refresh=true"
            : $"api/inventory/{steamId}";
        using var request = BuildRequest(HttpMethod.Get, path);

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

            if (payload is null || payload.Items is null)
            {
                // A 200 without an items array is a contract violation, not an
                // empty inventory. Failing safe here keeps it from being read
                // as "the asset is gone" one layer up (08 §2.3).
                _logger.LogWarning("Steam sidecar inventory returned an unusable body for {SteamId}", steamId);
                return new SteamSidecarInventoryResult(SteamSidecarStatus.Unavailable, Inventory: null);
            }

            // T121 — the sidecar carries the 08 §2.3 visibility in the body
            // ALONGSIDE the status code (T120). The status stays authoritative
            // (07 §6.1 is the normative public contract, and the field is
            // absent on older builds), but a 200 whose body says the inventory
            // was NOT readable is honoured rather than shipped upstream as an
            // empty inventory — that collapse is exactly what turns "profile
            // is private" into "item not in inventory".
            var declared = ParseVisibility(payload.Visibility);
            if (declared is not null && declared != SteamSidecarStatus.Success)
            {
                _logger.LogWarning(
                    "Steam sidecar returned 200 with visibility '{Visibility}' for {SteamId} — "
                    + "honouring the body over the status code",
                    payload.Visibility, steamId);
                return new SteamSidecarInventoryResult(declared.Value, Inventory: null);
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

    /// <summary>
    /// Map the sidecar's <c>visibility</c> field (08 §2.3) onto a status.
    /// Returns <c>null</c> when the field is absent — older sidecar builds do
    /// not emit it and the HTTP status alone is then authoritative.
    /// </summary>
    /// <remarks>
    /// An unrecognised value resolves to
    /// <see cref="SteamSidecarStatus.Unavailable"/>, never to
    /// <see cref="SteamSidecarStatus.Success"/>: a visibility this build does
    /// not understand is absence of information, and guessing "readable" is
    /// the one guess that can be mistaken for evidence.
    /// </remarks>
    private static SteamSidecarStatus? ParseVisibility(string? visibility)
    {
        if (string.IsNullOrWhiteSpace(visibility)) return null;

        return visibility.Trim().ToUpperInvariant() switch
        {
            "PUBLIC" => SteamSidecarStatus.Success,
            "PRIVATE" => SteamSidecarStatus.InventoryPrivate,
            _ => SteamSidecarStatus.Unavailable,
        };
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

    private sealed record SidecarInventoryEnvelope(
        [property: JsonPropertyName("items")] IReadOnlyList<SidecarInventoryItem>? Items,
        [property: JsonPropertyName("totalCount")] int TotalCount,
        [property: JsonPropertyName("tradeableCount")] int TradeableCount,
        // T120 added this alongside the status code; nullable because a
        // sidecar predating it simply omits the field (08 §2.3).
        [property: JsonPropertyName("visibility")] string? Visibility = null)
    {
        public SteamInventoryDto ToDto() => new(
            Items: (Items ?? []).Select(it => it.ToDto()).ToList(),
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
        [property: JsonPropertyName("marketable")] bool Marketable,
        // T125 — nullable because a sidecar predating the field simply omits
        // it. Absence must read as "not reported", never as "this asset has
        // none": the launch-gate reviewer distinguishes the two.
        [property: JsonPropertyName("assetProperties")]
        IReadOnlyList<SidecarAssetProperty>? AssetProperties = null)
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
            Marketable: Marketable)
        {
            AssetProperties = (AssetProperties ?? []).Select(p => p.ToDto()).ToList(),
        };
    }

    private sealed record SidecarAssetProperty(
        [property: JsonPropertyName("propertyId")] int PropertyId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("intValue")] string? IntValue,
        [property: JsonPropertyName("floatValue")] string? FloatValue,
        [property: JsonPropertyName("stringValue")] string? StringValue)
    {
        public SteamInventoryAssetPropertyDto ToDto() => new(
            PropertyId: PropertyId,
            Name: Name ?? string.Empty,
            IntValue: IntValue,
            FloatValue: FloatValue,
            StringValue: StringValue);
    }
}
