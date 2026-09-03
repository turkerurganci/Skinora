using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// <see cref="HttpClient"/>-backed implementation of
/// <see cref="IBlockchainTransferClient"/>. Routes
/// <c>BlockchainTransactionType</c> to the matching sidecar endpoint:
/// <list type="bullet">
///   <item><c>SELLER_PAYOUT</c> → <c>POST /api/transfer/payout</c></item>
///   <item><c>SWEEP</c> → <c>POST /api/transfer/sweep</c> (deposit → hot
///     wallet; the dispatcher resolves the deposit index/address and the row's
///     <c>ToAddress</c> carries the hot-wallet destination)</item>
///   <item>everything else (refund family) → <c>POST /api/transfer/refund</c></item>
/// </list>
/// Service-to-service auth piggybacks on the shared
/// <see cref="BlockchainSidecarOptions.InternalKey"/> via
/// <c>X-Internal-Key</c> (05 §3.4).
/// </summary>
public sealed class HttpBlockchainTransferClient : IBlockchainTransferClient
{
    public const string HttpClientName = "BlockchainTransfer";

    private const string InternalKeyHeader = "X-Internal-Key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly BlockchainSidecarOptions _options;
    private readonly ILogger<HttpBlockchainTransferClient> _logger;

    public HttpBlockchainTransferClient(
        HttpClient http,
        IOptions<BlockchainSidecarOptions> options,
        ILogger<HttpBlockchainTransferClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TransferBroadcastResult> BroadcastAsync(
        TransferBroadcastRequest request,
        CancellationToken cancellationToken)
    {
        var (path, body) = BuildRequest(request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        ApplyAuth(httpRequest);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "Blockchain sidecar broadcast transport failure for {BlockchainTransactionId} ({Type})",
                request.BlockchainTransactionId, request.Type);
            return new TransferBroadcastResult(
                TransferBroadcastStatus.TransientFailure, null, "TRANSPORT_FAILURE", ex.Message);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                TransferBroadcastResponse? payload = null;
                try
                {
                    payload = await response.Content.ReadFromJsonAsync<TransferBroadcastResponse>(
                        JsonOptions, cancellationToken);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex,
                        "Blockchain sidecar broadcast returned unparsable 200 body for {BlockchainTransactionId}",
                        request.BlockchainTransactionId);
                }
                if (payload is null || string.IsNullOrWhiteSpace(payload.TxHash))
                {
                    return new TransferBroadcastResult(
                        TransferBroadcastStatus.TransientFailure,
                        null,
                        "EMPTY_BODY",
                        "Sidecar returned 200 with empty txHash.");
                }
                return new TransferBroadcastResult(
                    TransferBroadcastStatus.Success, payload.TxHash, null, null);
            }

            ErrorEnvelope? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(
                    JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                // Sidecar may return non-JSON 5xx body — fall back to status text below.
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                _logger.LogError(
                    "Blockchain sidecar rejected broadcast for {BlockchainTransactionId} ({Type}): {Code} — {Message}",
                    request.BlockchainTransactionId, request.Type, error?.Error, error?.Message);
                return new TransferBroadcastResult(
                    TransferBroadcastStatus.InvalidRequest,
                    null,
                    error?.Error ?? "INVALID_REQUEST",
                    error?.Message);
            }

            _logger.LogWarning(
                "Blockchain sidecar broadcast {StatusCode} for {BlockchainTransactionId} ({Type}): {Code} — {Message}",
                (int)response.StatusCode, request.BlockchainTransactionId, request.Type,
                error?.Error, error?.Message);
            return new TransferBroadcastResult(
                TransferBroadcastStatus.TransientFailure,
                null,
                error?.Error ?? "TRANSPORT_FAILURE",
                error?.Message);
        }
    }

    public async Task<TransferStatusResult> GetStatusAsync(
        string txHash,
        CancellationToken cancellationToken)
    {
        var path = $"api/transfer/status/{Uri.EscapeDataString(txHash)}";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAuth(httpRequest);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "Blockchain sidecar status transport failure for {TxHash}", txHash);
            return new TransferStatusResult(
                TransferStatusOutcome.Unavailable, null, null, null, ex.Message);
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning(
                    "Blockchain sidecar status returned {StatusCode} for {TxHash}",
                    (int)response.StatusCode, txHash);
                return new TransferStatusResult(
                    TransferStatusOutcome.Unavailable, null, null, null, response.ReasonPhrase);
            }

            TransferStatusResponse? payload = null;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<TransferStatusResponse>(
                    JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                return new TransferStatusResult(
                    TransferStatusOutcome.Unavailable, null, null, null, ex.Message);
            }
            if (payload is null)
            {
                return new TransferStatusResult(
                    TransferStatusOutcome.Unavailable, null, null, null, "Empty status body");
            }

            if (payload.Confirmations >= 20)
            {
                var outcome = string.Equals(payload.ContractRet, "SUCCESS", StringComparison.OrdinalIgnoreCase)
                    ? TransferStatusOutcome.Confirmed
                    : TransferStatusOutcome.Failed;
                return new TransferStatusResult(
                    outcome,
                    payload.BlockNumber,
                    payload.Confirmations,
                    payload.ContractRet,
                    null,
                    payload.RealizedFeeSun,
                    payload.EnergyUsageTotal,
                    payload.OriginEnergyUsage);
            }
            return new TransferStatusResult(
                TransferStatusOutcome.Pending,
                payload.BlockNumber,
                payload.Confirmations,
                payload.ContractRet,
                null);
        }
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_options.InternalKey))
        {
            request.Headers.TryAddWithoutValidation(InternalKeyHeader, _options.InternalKey);
        }
    }

    private static (string Path, object Body) BuildRequest(TransferBroadcastRequest request)
    {
        var amountStr = request.Amount.ToString("0.######", CultureInfo.InvariantCulture);
        var tokenStr = request.Token.ToString();
        var idStr = request.BlockchainTransactionId.ToString("D");

        return request.Type switch
        {
            BlockchainTransactionType.SELLER_PAYOUT => (
                "api/transfer/payout",
                new PayoutBody(idStr, request.ToAddress, amountStr, tokenStr)),
            BlockchainTransactionType.BUYER_PAYMENT or BlockchainTransactionType.WRONG_TOKEN_INCOMING
                or BlockchainTransactionType.SPAM_TOKEN_INCOMING =>
                throw new InvalidOperationException(
                    $"Type {request.Type} is incoming — dispatcher should not broadcast it."),
            // WP3 — sweep is deposit → hot wallet. Same deposit-sourced signing
            // model as a refund (deposit index/address), but the destination is
            // the platform hot wallet (the row's ToAddress) and the sidecar runs
            // it through energy delegation, so it gets its own endpoint + body.
            BlockchainTransactionType.SWEEP => (
                "api/transfer/sweep",
                new SweepBody(
                    idStr,
                    request.DepositIndex ?? throw new InvalidOperationException(
                        "DepositIndex is required for SWEEP."),
                    request.DepositAddress ?? throw new InvalidOperationException(
                        "DepositAddress is required for SWEEP."),
                    request.ToAddress,
                    amountStr,
                    tokenStr)),
            _ => (
                "api/transfer/refund",
                new RefundBody(
                    idStr,
                    request.DepositIndex ?? throw new InvalidOperationException(
                        $"DepositIndex is required for {request.Type}."),
                    request.DepositAddress ?? throw new InvalidOperationException(
                        $"DepositAddress is required for {request.Type}."),
                    request.ToAddress,
                    amountStr,
                    tokenStr)),
        };
    }

    private sealed record PayoutBody(
        [property: JsonPropertyName("blockchainTransactionId")] string BlockchainTransactionId,
        [property: JsonPropertyName("toAddress")] string ToAddress,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("token")] string Token);

    private sealed record RefundBody(
        [property: JsonPropertyName("blockchainTransactionId")] string BlockchainTransactionId,
        [property: JsonPropertyName("depositIndex")] int DepositIndex,
        [property: JsonPropertyName("depositAddress")] string DepositAddress,
        [property: JsonPropertyName("toBuyerAddress")] string ToBuyerAddress,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("token")] string Token);

    private sealed record SweepBody(
        [property: JsonPropertyName("blockchainTransactionId")] string BlockchainTransactionId,
        [property: JsonPropertyName("depositIndex")] int DepositIndex,
        [property: JsonPropertyName("depositAddress")] string DepositAddress,
        [property: JsonPropertyName("toHotWalletAddress")] string ToHotWalletAddress,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("token")] string Token);

    private sealed record TransferBroadcastResponse(
        [property: JsonPropertyName("txHash")] string? TxHash);

    private sealed record TransferStatusResponse(
        [property: JsonPropertyName("txHash")] string? TxHash,
        [property: JsonPropertyName("blockNumber")] long? BlockNumber,
        [property: JsonPropertyName("contractRet")] string? ContractRet,
        [property: JsonPropertyName("confirmations")] int Confirmations,
        [property: JsonPropertyName("realizedFeeSun")] long? RealizedFeeSun = null,
        [property: JsonPropertyName("energyUsageTotal")] long? EnergyUsageTotal = null,
        [property: JsonPropertyName("originEnergyUsage")] long? OriginEnergyUsage = null);

    private sealed record ErrorEnvelope(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("retryable")] bool? Retryable);
}
