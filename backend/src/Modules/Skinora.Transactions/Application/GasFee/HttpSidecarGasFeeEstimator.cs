using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
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
/// <remarks>
/// One instance per network (T148 — 05 §3.3a), built by
/// <c>BlockchainServiceRegistry</c>; the body names the network. The
/// breakdown fields of the answer (Energy, SUN burned, TRX price) are TRON's
/// and are only logged for a network that reports them. Ethereum's estimate
/// is wei (T154): its answer carries <c>feeWei</c> and <c>ethPriceUsdt</c> as
/// decimal strings, and the charged <c>feeUsdt</c> is accepted only if it is
/// exactly their rounded-up product (<see cref="WeiAmount"/>).
/// </remarks>
public sealed class HttpSidecarGasFeeEstimator : IGasFeeEstimator
{
    /// <summary>TRON's named HTTP client (pre-F8 name, kept); see <see cref="Networks.BlockchainHttpClientNames"/>.</summary>
    public const string HttpClientName = "BlockchainGasFeeEstimate";

    private const string InternalKeyHeader = "X-Internal-Key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BlockchainNetworkPolicy _policy;
    private readonly HttpClient _http;
    private readonly BlockchainSidecarOptions _options;
    private readonly ILogger<HttpSidecarGasFeeEstimator> _logger;

    public HttpSidecarGasFeeEstimator(
        BlockchainNetwork network,
        HttpClient http,
        BlockchainSidecarOptions options,
        ILogger<HttpSidecarGasFeeEstimator> logger)
    {
        _policy = BlockchainNetworkPolicy.For(network);
        _http = http;
        _options = options;
        _logger = logger;
    }

    /// <summary>The network whose service this estimator asks.</summary>
    public BlockchainNetwork Network => _policy.Network;

    public async Task<decimal?> EstimateFeeUsdtAsync(
        GasFeeEstimateRequest request, CancellationToken cancellationToken)
    {
        var body = new EstimateFeeBody(
            _policy.Network.ToString(),
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

            var answer = await response.Content.ReadFromJsonAsync<JsonElement>(
                JsonOptions, cancellationToken);
            var payload = answer.ValueKind == JsonValueKind.Object
                ? answer.Deserialize<EstimateFeeResponse>(JsonOptions)
                : null;
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

            if (_policy.ReportsTronResourceUsage)
            {
                _logger.LogInformation(
                    "Gas fee estimate {FeeUsdt} USDT for {To} ({Token}): energy {EnergyShortfall}/{EnergyRequired} short, burn {BurnSun} sun, TRX price {TrxPrice} ({PriceSource}).",
                    fee, request.ToAddress, request.Token, payload.EnergyShortfall,
                    payload.EnergyRequired, payload.BurnSun, payload.TrxPriceUsdt, payload.PriceSource);
            }
            else if (_policy.ReportsFeeInWei)
            {
                // T154 — the wei figure is the estimate; feeUsdt is only its
                // conversion. Re-derive it here with exact integer arithmetic:
                // an answer whose two figures disagree (a unit slip, a lossy
                // number on either side) is not charged — the fallback is.
                var wei = answer.Deserialize<WeiFeeFields>(JsonOptions);
                if (wei is null
                    || !WeiAmount.TryUsdtCentsCeil(wei.FeeWei, wei.EthPriceUsdt, out var derived)
                    || derived != fee)
                {
                    _logger.LogError(
                        "Gas fee estimate on {Network} for {To} ({Token}): feeUsdt {FeeUsdt} does not follow from feeWei '{FeeWei}' at ETH price '{EthPrice}' — falling back to static setting.",
                        _policy.Network, request.ToAddress, request.Token, fee, wei?.FeeWei, wei?.EthPriceUsdt);
                    return null;
                }

                _logger.LogInformation(
                    "Gas fee estimate {FeeUsdt} USDT for {To} ({Token}) on {Network}: {FeeWei} wei ({Sender}, gas {GasLimit} + fuel {FuelGasLimit}, max {MaxFeePerGasWei} wei/gas) at ETH price {EthPrice} ({PriceSource}).",
                    fee, request.ToAddress, request.Token, _policy.Network, wei.FeeWei, wei.Sender,
                    wei.GasLimit, wei.FuelGasLimit, wei.MaxFeePerGasWei, wei.EthPriceUsdt, wei.PriceSource);
            }
            else
            {
                _logger.LogInformation(
                    "Gas fee estimate {FeeUsdt} USDT for {To} ({Token}) on {Network}.",
                    fee, request.ToAddress, request.Token, _policy.Network);
            }
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

    // The sidecar answers every rejection with {error, message}. The status
    // separates a rejected request (400) from a retryable outage (502); the
    // code says which one it was — INVALID_ESTIMATE_REQUEST, a malformed
    // request, recurs on every call. Best-effort: a body that cannot be read
    // still falls back, it just logs no code. That covers more than JSON
    // errors — a proxy's HTML page throws JsonException, a charset .NET cannot
    // decode InvalidOperationException, UTF-7 NotSupportedException — and none
    // of them may turn a fallback into an exception on a money path (#327
    // validation). Cancellation still propagates.
    private static async Task<ErrorEnvelope?> ReadErrorAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorEnvelope>(
                JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
        string Network,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FromAddress,
        string ToAddress,
        string Amount,
        string Token);

    private sealed record ErrorEnvelope(string? Error, string? Message);

    // Everything but FeeUsdt is TRON's breakdown (Energy, SUN, TRX price) —
    // nullable so a network whose answer does not carry it still parses.
    private sealed record EstimateFeeResponse(
        string? FeeUsdt,
        long? EnergyRequired,
        long? EnergyAvailable,
        long? EnergyShortfall,
        long? BurnSun,
        decimal? TrxPriceUsdt,
        string? PriceSource);

    // T154 — Ethereum's breakdown. Every integer is a decimal string (wei
    // passes 2^53); the example body is sidecar-ethereum/contracts/estimate-fee/response.json.
    private sealed record WeiFeeFields(
        string? FeeWei,
        string? EthPriceUsdt,
        string? Sender,
        string? GasLimit,
        string? FuelGasLimit,
        string? MaxFeePerGasWei,
        string? PriceSource);
}
