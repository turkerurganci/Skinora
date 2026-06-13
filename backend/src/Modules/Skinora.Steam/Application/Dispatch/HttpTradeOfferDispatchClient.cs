using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Steam.Application.Inventory;

namespace Skinora.Steam.Application.Dispatch;

/// <summary>
/// <see cref="HttpClient"/>-backed <see cref="ITradeOfferDispatchClient"/>
/// (T106a). Posts to the Steam sidecar's <c>POST /api/trade-offers/send</c>
/// (08 §2.7). Shares <see cref="SteamSidecarOptions"/> with the inventory
/// client — same container, same <c>X-Internal-Key</c> auth (05 §3.4) — but
/// is registered as its own typed client so the trade endpoint can carry a
/// longer timeout (the sidecar may run a 5/15/45s internal retry before
/// answering, 08 §2.7).
/// </summary>
/// <remarks>
/// HTTP → status mapping mirrors <c>HttpBlockchainSidecarClient</c>:
/// 200 → Sent/Pending (from body), 502 → Failed (body <c>retryable</c>),
/// 400 → Failed non-retryable (payload disagreement), 503/5xx/transport →
/// Unavailable (transient). The body matches the sidecar
/// <c>SendTradeOfferResponse</c>.
/// </remarks>
public sealed class HttpTradeOfferDispatchClient : ITradeOfferDispatchClient
{
    /// <summary>HTTP client name used by <c>AddHttpClient</c>.</summary>
    public const string HttpClientName = "SteamSidecarTradeOffers";

    private const string InternalKeyHeader = "X-Internal-Key";
    private const string SendPath = "api/trade-offers/send";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly SteamSidecarOptions _options;
    private readonly ILogger<HttpTradeOfferDispatchClient> _logger;

    public HttpTradeOfferDispatchClient(
        HttpClient http,
        IOptions<SteamSidecarOptions> options,
        ILogger<HttpTradeOfferDispatchClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TradeOfferDispatchResult> SendAsync(
        TradeOfferDispatchRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, SendPath)
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        if (!string.IsNullOrEmpty(_options.InternalKey))
        {
            httpRequest.Headers.TryAddWithoutValidation(InternalKeyHeader, _options.InternalKey);
        }
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "Trade-offer dispatch transport failure for transaction {TransactionId} ({Direction})",
                request.TransactionId, request.Direction);
            return Unavailable("SIDECAR_UNREACHABLE");
        }

        using (response)
        {
            SidecarSendResponse? body = null;
            try
            {
                body = await response.Content
                    .ReadFromJsonAsync<SidecarSendResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Trade-offer dispatch response could not be parsed for transaction {TransactionId} (HTTP {Status})",
                    request.TransactionId, (int)response.StatusCode);
            }

            var attempts = body?.Attempts ?? 0;

            // 503 / 5xx — sidecar not ready or transient upstream; retry next tick.
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable
                || (int)response.StatusCode >= 500 && response.StatusCode != HttpStatusCode.BadGateway)
            {
                _logger.LogWarning(
                    "Trade-offer dispatch sidecar unavailable (HTTP {Status}) for transaction {TransactionId}",
                    (int)response.StatusCode, request.TransactionId);
                return Unavailable(body?.Reason ?? $"HTTP_{(int)response.StatusCode}", attempts);
            }

            // 400 — payload disagreement; resending the same body will keep failing.
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                _logger.LogError(
                    "Trade-offer dispatch rejected as invalid (HTTP 400) for transaction {TransactionId}: {Reason}",
                    request.TransactionId, body?.Reason);
                return new TradeOfferDispatchResult(
                    TradeOfferDispatchStatus.Failed, null, Retryable: false, attempts,
                    body?.Reason ?? "INVALID_REQUEST");
            }

            if (body is null)
            {
                _logger.LogWarning(
                    "Trade-offer dispatch returned empty body (HTTP {Status}) for transaction {TransactionId}",
                    (int)response.StatusCode, request.TransactionId);
                return Unavailable("EMPTY_BODY");
            }

            return body.Status switch
            {
                "sent" or "confirmed" => new TradeOfferDispatchResult(
                    TradeOfferDispatchStatus.Sent, body.OfferId, Retryable: false, attempts, null),
                "pending" => new TradeOfferDispatchResult(
                    TradeOfferDispatchStatus.Pending, body.OfferId, Retryable: false, attempts, null),
                // "failed" (HTTP 502) — sidecar tried and could not send.
                _ => new TradeOfferDispatchResult(
                    TradeOfferDispatchStatus.Failed, body.OfferId, body.Retryable ?? false, attempts,
                    body.Reason),
            };
        }
    }

    private static TradeOfferDispatchResult Unavailable(string reason, int attempts = 0)
        => new(TradeOfferDispatchStatus.Unavailable, null, Retryable: true, attempts, reason);

    private sealed record SidecarSendResponse(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("offerId")] string? OfferId,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("retryable")] bool? Retryable,
        [property: JsonPropertyName("attempts")] int Attempts);
}
