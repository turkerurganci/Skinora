using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.Transactions.Application.GasFee;

/// <summary>
/// <see cref="HttpClient"/>-backed <see cref="IGasFeeEstimator"/> against the
/// blockchain sidecar's <c>POST /api/transfer/estimate-fee</c>. Every failure
/// shape — transport, non-200, unparsable body — collapses to <c>null</c>
/// (logged), because the contract is "estimate or fall back", never "estimate
/// or block": the money paths that call this were shipping with a static
/// constant before this round and must keep working when the estimator is
/// down. Auth mirrors <see cref="Transfers.HttpBlockchainTransferClient"/>
/// (<c>X-Internal-Key</c>, 05 §3.4).
/// </summary>
public sealed class HttpSidecarGasFeeEstimator : IGasFeeEstimator
{
    public const string HttpClientName = "BlockchainGasFeeEstimate";

    private const string InternalKeyHeader = "X-Internal-Key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly BlockchainSidecarOptions _options;
    private readonly ILogger<HttpSidecarGasFeeEstimator> _logger;

    public HttpSidecarGasFeeEstimator(
        HttpClient http,
        IOptions<BlockchainSidecarOptions> options,
        ILogger<HttpSidecarGasFeeEstimator> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<decimal?> EstimateFeeUsdtAsync(
        GasFeeEstimateRequest request, CancellationToken cancellationToken)
    {
        var body = new EstimateFeeBody(
            request.FromAddress,
            request.ToAddress,
            request.Amount.ToString("0.######", CultureInfo.InvariantCulture),
            request.Token.ToString());

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post, "api/transfer/estimate-fee")
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        if (!string.IsNullOrWhiteSpace(_options.InternalKey))
        {
            httpRequest.Headers.TryAddWithoutValidation(InternalKeyHeader, _options.InternalKey);
        }

        try
        {
            using var response = await _http.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorAsync(response, cancellationToken);
                _logger.LogWarning(
                    "Gas fee estimate returned HTTP {StatusCode} {ErrorCode} for {To} ({Token}): {ErrorMessage} — falling back to static setting.",
                    (int)response.StatusCode, error?.Error, request.ToAddress, request.Token, error?.Message);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<EstimateFeeResponse>(
                JsonOptions, cancellationToken);
            if (payload is null
                || !decimal.TryParse(
                    payload.FeeUsdt, NumberStyles.Number, CultureInfo.InvariantCulture, out var fee)
                || fee < 0m)
            {
                _logger.LogWarning(
                    "Gas fee estimate returned unparsable feeUsdt '{FeeUsdt}' — falling back to static setting.",
                    payload?.FeeUsdt);
                return null;
            }

            _logger.LogInformation(
                "Gas fee estimate {FeeUsdt} USDT for {To} ({Token}): energy {EnergyShortfall}/{EnergyRequired} short, burn {BurnSun} sun, TRX price {TrxPrice} ({PriceSource}).",
                fee, request.ToAddress, request.Token, payload.EnergyShortfall,
                payload.EnergyRequired, payload.BurnSun, payload.TrxPriceUsdt, payload.PriceSource);
            return fee;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex,
                "Gas fee estimate call failed for {To} ({Token}) — falling back to static setting.",
                request.ToAddress, request.Token);
            return null;
        }
    }

    // The sidecar answers every rejection with {error, message}. The code is
    // what separates "this request is malformed" (INVALID_ESTIMATE_REQUEST —
    // recurs on every call) from a price-feed outage; with the bare status
    // code alone, every payout estimate fell back unnoticed until a live
    // rehearsal (PayoutGasEstimateAlwaysFallsBack). Best-effort: a proxy page
    // in place of the envelope still falls back, it just logs no code.
    private static async Task<ErrorEnvelope?> ReadErrorAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorEnvelope>(
                JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    // FromAddress is omitted when null, never sent as null: the sidecar reads
    // an absent sender as "the hot wallet" (the payout path) but rejects an
    // explicit JSON null with 400. The bodies both sides must agree on live in
    // sidecar-blockchain/contracts/estimate-fee/ and are replayed by
    // BlockchainSidecarEstimateFeeContractTests and the sidecar handler test.
    private sealed record EstimateFeeBody(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FromAddress,
        string ToAddress,
        string Amount,
        string Token);

    private sealed record ErrorEnvelope(string? Error, string? Message);

    private sealed record EstimateFeeResponse(
        string? FeeUsdt,
        long EnergyRequired,
        long EnergyAvailable,
        long EnergyShortfall,
        long BurnSun,
        decimal TrxPriceUsdt,
        string? PriceSource);
}
