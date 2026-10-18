using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Blockchain;
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
/// Service-to-service auth uses the network's own
/// <see cref="BlockchainSidecarOptions.InternalKey"/> via
/// <c>X-Internal-Key</c> (05 §3.4).
/// </summary>
/// <remarks>
/// One instance per network (T148 — 05 §3.3a), built by
/// <c>BlockchainServiceRegistry</c>. Every broadcast body names the network so
/// a service reached at the wrong address refuses it, and the status read
/// decides finality with the network's <see cref="BlockchainNetworkPolicy"/>
/// instead of a hardcoded 20 blocks: TRON counts blocks, Ethereum waits for
/// <c>finalized</c> (11 §F8 K5). TRON's Energy / SUN receipt fields are only
/// passed on for a network that reports them.
/// </remarks>
public sealed class HttpBlockchainTransferClient : IBlockchainTransferClient
{
    /// <summary>TRON's named HTTP client (pre-F8 name, kept); see <see cref="Networks.BlockchainHttpClientNames"/>.</summary>
    public const string HttpClientName = "BlockchainTransfer";

    private const string InternalKeyHeader = "X-Internal-Key";

    /// <summary>
    /// Error code of a broadcast that never got an answer from the service
    /// (connection refused, timeout, a non-JSON 5xx). The dispatch job reads it
    /// as "this network's service is unreachable" and leaves the network's
    /// other rows for the next run (T150 — <c>NetworkWorkQueue</c>).
    /// </summary>
    public const string TransportFailureCode = "TRANSPORT_FAILURE";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BlockchainNetworkPolicy _policy;
    private readonly string _networkWire;
    private readonly HttpClient _http;
    private readonly BlockchainSidecarOptions _options;
    private readonly ILogger<HttpBlockchainTransferClient> _logger;

    public HttpBlockchainTransferClient(
        BlockchainNetwork network,
        HttpClient http,
        BlockchainSidecarOptions options,
        ILogger<HttpBlockchainTransferClient> logger)
    {
        _policy = BlockchainNetworkPolicy.For(network);
        _networkWire = network.ToString();
        _http = http;
        _options = options;
        _logger = logger;
    }

    /// <summary>The network whose service this client talks to.</summary>
    public BlockchainNetwork Network => _policy.Network;

    public async Task<TransferBroadcastResult> BroadcastAsync(
        TransferBroadcastRequest request,
        CancellationToken cancellationToken)
    {
        var (path, body) = BuildRequest(_networkWire, request);
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
                TransferBroadcastStatus.TransientFailure, null, TransportFailureCode, ex.Message);
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
                error?.Error ?? TransportFailureCode,
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

        // The client's own timeout is the broadcast budget, which waits for
        // blocks; a status read is one chain lookup and keeps the chain-read
        // budget so a stuck call cannot hold the confirmation job for minutes.
        using var readBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readBudget.CancelAfter(_options.ResolveChainReadTimeout());

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, readBudget.Token);
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

            // T148 — finality is the network's rule, not a TRON constant:
            // TRON needs 20 blocks, Ethereum an explicit `finalized: true`
            // (a block count alone never qualifies there — 11 §F8 K5).
            if (_policy.IsFinal(payload.Confirmations, payload.Finalized))
            {
                var outcome = string.Equals(payload.ContractRet, "SUCCESS", StringComparison.OrdinalIgnoreCase)
                    ? TransferStatusOutcome.Confirmed
                    : TransferStatusOutcome.Failed;
                var tronResources = _policy.ReportsTronResourceUsage;
                return new TransferStatusResult(
                    outcome,
                    payload.BlockNumber,
                    payload.Confirmations,
                    payload.ContractRet,
                    null,
                    tronResources ? payload.RealizedFeeSun : null,
                    tronResources ? payload.EnergyUsageTotal : null,
                    tronResources ? payload.OriginEnergyUsage : null);
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

    private static (string Path, object Body) BuildRequest(string network, TransferBroadcastRequest request)
    {
        var amountStr = request.Amount.ToString("0.######", CultureInfo.InvariantCulture);
        var tokenStr = request.Token.ToString();
        var idStr = request.BlockchainTransactionId.ToString("D");

        return request.Type switch
        {
            BlockchainTransactionType.SELLER_PAYOUT => (
                "api/transfer/payout",
                new PayoutBody(network, idStr, request.ToAddress, amountStr, tokenStr)),
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
                    network,
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
                    network,
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
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("blockchainTransactionId")] string BlockchainTransactionId,
        [property: JsonPropertyName("toAddress")] string ToAddress,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("token")] string Token);

    private sealed record RefundBody(
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("blockchainTransactionId")] string BlockchainTransactionId,
        [property: JsonPropertyName("depositIndex")] int DepositIndex,
        [property: JsonPropertyName("depositAddress")] string DepositAddress,
        [property: JsonPropertyName("toBuyerAddress")] string ToBuyerAddress,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("token")] string Token);

    private sealed record SweepBody(
        [property: JsonPropertyName("network")] string Network,
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
        [property: JsonPropertyName("confirmations")] int? Confirmations,
        [property: JsonPropertyName("finalized")] bool? Finalized = null,
        [property: JsonPropertyName("realizedFeeSun")] long? RealizedFeeSun = null,
        [property: JsonPropertyName("energyUsageTotal")] long? EnergyUsageTotal = null,
        [property: JsonPropertyName("originEnergyUsage")] long? OriginEnergyUsage = null);

    private sealed record ErrorEnvelope(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("retryable")] bool? Retryable);
}
