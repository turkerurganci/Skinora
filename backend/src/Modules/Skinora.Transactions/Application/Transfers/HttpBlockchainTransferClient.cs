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

    /// <summary>
    /// T158 (g) — the Ethereum service's <c>contractRet</c> for a mined hash
    /// its journal does not hold whose Transfer log does not match the row
    /// (<c>sidecar-ethereum/contracts/transfer-status/unknown.response.json</c>).
    /// </summary>
    public const string UnverifiedContractRet = "UNKNOWN";

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
                // T160c düzeltme turu — TRON deposit sends report what they cost
                // the hot wallet; a negative value is not a measurement.
                return new TransferBroadcastResult(
                    TransferBroadcastStatus.Success, payload.TxHash, null, null,
                    _policy.ReportsTronResourceUsage
                        ? ReadPlatformSpendSun(payload.PlatformSpendSun, request.BlockchainTransactionId)
                        : null);
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

            // T152 — "not now, nothing signed" (fee above the cap, no ETH price,
            // no gas on the sender — 08 §3.7.7). Keyed on the body's flag, not
            // on the status code, so no other 5xx can be mistaken for a wait.
            if (error?.Deferred == true)
            {
                _logger.LogInformation(
                    "Blockchain sidecar deferred broadcast for {BlockchainTransactionId} ({Type}): {Code} — {Message} (waiting since {WaitingSince})",
                    request.BlockchainTransactionId, request.Type, error.Error, error.Message, error.WaitingSince);
                return new TransferBroadcastResult(
                    TransferBroadcastStatus.Deferred,
                    null,
                    error.Error ?? "DEFERRED",
                    error.WaitingSince is null ? error.Message : $"{error.Message} (waiting since {error.WaitingSince})");
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

    public Task<TransferStatusResult> GetStatusAsync(
        string txHash,
        CancellationToken cancellationToken) =>
        GetStatusAsync(txHash, expected: null, cancellationToken);

    public async Task<TransferStatusResult> GetStatusAsync(
        string txHash,
        TransferStatusExpectation? expected,
        CancellationToken cancellationToken)
    {
        var path = $"api/transfer/status/{Uri.EscapeDataString(txHash)}";
        // T158 (g) — only for a network whose service verifies a hash its
        // journal does not hold (Ethereum); TRON's request is unchanged.
        if (expected is not null && _policy.VerifiesUnjournalledTransferStatus)
        {
            path += "?contract=" + Uri.EscapeDataString(expected.Contract)
                + "&from=" + Uri.EscapeDataString(expected.FromAddress)
                + "&to=" + Uri.EscapeDataString(expected.ToAddress)
                + "&amount=" + Uri.EscapeDataString(
                    expected.Amount.ToString("0.######", CultureInfo.InvariantCulture));
        }
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
                // T152 (T150 doğrulaması) — a 4xx about THIS hash (unknown,
                // nonce conflict, malformed) is row-specific and must not end
                // the network's turn in the confirmation job. A 4xx that says
                // the SERVICE is wrong for every row (auth, timeout, rate
                // limit) and every 5xx is the network's.
                return new TransferStatusResult(
                    IsRowSpecific(response.StatusCode)
                        ? TransferStatusOutcome.Rejected
                        : TransferStatusOutcome.Unavailable,
                    null, null, null, $"{(int)response.StatusCode} {response.ReasonPhrase}");
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
                // T158 (g) — UNKNOWN: final, but not provably this row's
                // transfer. Neither a success nor a failure (fail closed).
                var outcome = string.Equals(payload.ContractRet, "SUCCESS", StringComparison.OrdinalIgnoreCase)
                    ? TransferStatusOutcome.Confirmed
                    : string.Equals(payload.ContractRet, UnverifiedContractRet, StringComparison.OrdinalIgnoreCase)
                        ? TransferStatusOutcome.Unverified
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
                    tronResources ? payload.OriginEnergyUsage : null,
                    TxHash: string.IsNullOrWhiteSpace(payload.TxHash) ? null : payload.TxHash,
                    RealizedFeeWei: _policy.ReportsFeeInWei ? ReadRealizedFeeWei(payload.RealizedFeeWei, txHash) : null,
                    PlatformSpendWei: _policy.ReportsFeeInWei ? ReadWei(payload.PlatformSpendWei, "platformSpendWei", txHash) : null,
                    NativePriceUsdt: ReadPrice(payload.NativePriceUsdt, txHash));
            }
            return new TransferStatusResult(
                TransferStatusOutcome.Pending,
                payload.BlockNumber,
                payload.Confirmations,
                payload.ContractRet,
                null);
        }
    }

    public async Task<TransferVerifyResult> VerifyTransferAsync(
        TransferVerifyRequest request,
        CancellationToken cancellationToken)
    {
        // T157 — the Ethereum service; T172a — the TRON service answers the
        // same read (solidified transaction, TRC-20 Transfer log). A network
        // added later without it is not asked.
        if (_policy.Network is not (BlockchainNetwork.ETHEREUM or BlockchainNetwork.TRON))
        {
            return new TransferVerifyResult(TransferVerifyOutcome.NotSupported, null, "Not supported by this network's service.");
        }

        var body = new VerifyBody(
            _networkWire,
            request.TxHash,
            request.Token.ToString(),
            request.From,
            request.To,
            request.Amount.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/transfer/verify")
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        ApplyAuth(httpRequest);

        // One chain lookup: the chain-read budget, not the broadcast budget.
        using var readBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readBudget.CancelAfter(_options.ResolveChainReadTimeout());

        try
        {
            using var response = await _http.SendAsync(httpRequest, readBudget.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning(
                    "Blockchain sidecar verify returned {StatusCode} for {TxHash}",
                    (int)response.StatusCode, request.TxHash);
                return new TransferVerifyResult(
                    TransferVerifyOutcome.Unavailable, null,
                    $"{(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var payload = await response.Content
                .ReadFromJsonAsync<VerifyResponse>(JsonOptions, cancellationToken);
            if (payload is null || string.IsNullOrEmpty(payload.Outcome))
            {
                return new TransferVerifyResult(TransferVerifyOutcome.Unavailable, null, "Empty verify body");
            }

            // Anything but an exact, known outcome is "not verified" — and the
            // `matches` flag must agree with the outcome name (a service that
            // says MATCH with matches:false, or the reverse, is not believed).
            var outcome = payload.Outcome switch
            {
                "MATCH" when payload.Matches == true => TransferVerifyOutcome.Match,
                "NO_MATCHING_TRANSFER" when payload.Matches != true => TransferVerifyOutcome.NoMatchingTransfer,
                "REVERTED" when payload.Matches != true => TransferVerifyOutcome.Reverted,
                "NOT_FOUND" when payload.Matches != true => TransferVerifyOutcome.NotFound,
                _ => TransferVerifyOutcome.Unavailable,
            };
            return new TransferVerifyResult(outcome, payload.BlockNumber, payload.Outcome);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Blockchain sidecar verify failed for {TxHash}", request.TxHash);
            return new TransferVerifyResult(TransferVerifyOutcome.Unavailable, null, ex.Message);
        }
    }

    /// <summary>A 4xx about the asked transaction, not about the service (T152).</summary>
    private static bool IsRowSpecific(HttpStatusCode code)
    {
        var value = (int)code;
        return value is >= 400 and < 500
            && code is not HttpStatusCode.Unauthorized
            && code is not HttpStatusCode.Forbidden
            && code is not HttpStatusCode.RequestTimeout
            && code is not HttpStatusCode.TooManyRequests;
    }

    // T154 — the realized fee is measurement, never a gate: a missing or
    // malformed value (a JSON number, a hex string, more digits than decimal
    // holds exactly) is logged and recorded as NULL ("not measured"); it never
    // turns a final transfer into an unreadable status.
    private decimal? ReadRealizedFeeWei(JsonElement? raw, string txHash) =>
        ReadWei(raw, "realizedFeeWei", txHash);

    private decimal? ReadWei(JsonElement? raw, string field, string txHash)
    {
        if (raw is not { } value || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String
            && WeiAmount.TryParse(value.GetString(), out var wei))
        {
            return wei;
        }
        _logger.LogWarning(
            "{Network} status for {TxHash} carries an unusable {Field} {Raw} (a decimal string of at most {MaxDigits} digits is expected) — not recorded.",
            _policy.Network, txHash, field, value.GetRawText(), WeiAmount.MaxDigits);
        return null;
    }

    private long? ReadPlatformSpendSun(JsonElement? raw, Guid rowId)
    {
        if (raw is not { } value || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var sun) && sun >= 0)
        {
            return sun;
        }
        _logger.LogWarning(
            "{Network} broadcast of {BlockchainTransactionId} carries an unusable platformSpendSun {Raw} — not recorded.",
            _policy.Network, rowId, value.GetRawText());
        return null;
    }

    // T160c düzeltme turu — the price the platform spend is valued at. Same
    // rule as the realized fee: measurement, never a gate. A plain decimal
    // string, positive, at most 6 decimals (both services send `toFixed(6)`).
    private decimal? ReadPrice(JsonElement? raw, string txHash)
    {
        if (raw is not { } value || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price)
            && price > 0m
            && decimal.Round(price, 6) == price)
        {
            return price;
        }
        _logger.LogWarning(
            "{Network} status for {TxHash} carries an unusable nativePriceUsdt {Raw} — the platform spend is not valued.",
            _policy.Network, txHash, value.GetRawText());
        return null;
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
        // T152 — only for a network with a fee cap (Ethereum: payout, sweep and,
        // since the düzeltme turu, refunds);
        // omitted from the body otherwise, so TRON's service sees no change.
        var feeCapStr = request.FeeCapUsd?.ToString("0.######", CultureInfo.InvariantCulture);

        return request.Type switch
        {
            BlockchainTransactionType.SELLER_PAYOUT => (
                "api/transfer/payout",
                new PayoutBody(network, idStr, request.ToAddress, amountStr, tokenStr, feeCapStr)),
            BlockchainTransactionType.BUYER_PAYMENT or BlockchainTransactionType.WRONG_TOKEN_INCOMING
                or BlockchainTransactionType.SPAM_TOKEN_INCOMING =>
                throw new InvalidOperationException(
                    $"Type {request.Type} is incoming — dispatcher should not broadcast it."),
            // WP3 — sweep is deposit → hot wallet. Same deposit-sourced signing
            // model as a refund (deposit index/address), but the destination is
            // the platform hot wallet (the row's ToAddress) and the sidecar runs
            // it through energy delegation, so it gets its own endpoint + body.
            // T160c — REFUND_FEE_SWEEP is the same deposit → hot wallet send
            // (pinned destination), carrying withheld refund fees.
            BlockchainTransactionType.SWEEP or BlockchainTransactionType.REFUND_FEE_SWEEP => (
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
                    tokenStr,
                    feeCapStr)),
            // T158 (f) — a refund after the sweep: the hot wallet signs, no deposit.
            _ when request.FromHotWallet => (
                "api/transfer/refund",
                new HotWalletRefundBody(
                    network,
                    idStr,
                    "HOT_WALLET",
                    request.ToAddress,
                    amountStr,
                    tokenStr,
                    request.Type.ToString(),
                    feeCapStr)),
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
                    tokenStr,
                    // T152 — the Ethereum signer moves USDC only for a
                    // WRONG_TOKEN_REFUND (08 §3.7.6); TRON's service ignores it.
                    request.Type.ToString(),
                    feeCapStr)),
        };
    }

    private sealed record PayoutBody(
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("blockchainTransactionId")] string BlockchainTransactionId,
        [property: JsonPropertyName("toAddress")] string ToAddress,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("feeCapUsd"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FeeCapUsd);

    private sealed record RefundBody(
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("blockchainTransactionId")] string BlockchainTransactionId,
        [property: JsonPropertyName("depositIndex")] int DepositIndex,
        [property: JsonPropertyName("depositAddress")] string DepositAddress,
        [property: JsonPropertyName("toBuyerAddress")] string ToBuyerAddress,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("refundType")] string RefundType,
        [property: JsonPropertyName("feeCapUsd"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FeeCapUsd);

    /// <summary>T158 (f) — <c>sidecar-ethereum/contracts/transfer-refund/hot-wallet.request.json</c>.</summary>
    private sealed record HotWalletRefundBody(
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("blockchainTransactionId")] string BlockchainTransactionId,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("toBuyerAddress")] string ToBuyerAddress,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("refundType")] string RefundType,
        [property: JsonPropertyName("feeCapUsd"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FeeCapUsd);

    private sealed record SweepBody(
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("blockchainTransactionId")] string BlockchainTransactionId,
        [property: JsonPropertyName("depositIndex")] int DepositIndex,
        [property: JsonPropertyName("depositAddress")] string DepositAddress,
        [property: JsonPropertyName("toHotWalletAddress")] string ToHotWalletAddress,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("feeCapUsd"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FeeCapUsd);

    private sealed record VerifyBody(
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("txHash")] string TxHash,
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("from"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? From,
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("amount")] string Amount);

    private sealed record VerifyResponse(
        [property: JsonPropertyName("outcome")] string? Outcome,
        [property: JsonPropertyName("matches")] bool? Matches,
        [property: JsonPropertyName("blockNumber")] long? BlockNumber);

    private sealed record TransferBroadcastResponse(
        [property: JsonPropertyName("txHash")] string? TxHash,
        // T160c düzeltme turu — TRON sweep / refund (sidecar-blockchain transferHandlers).
        // Read as an element: a malformed measurement must never turn a
        // broadcast that happened into an unparsable (retried) answer.
        [property: JsonPropertyName("platformSpendSun")] JsonElement? PlatformSpendSun = null);

    private sealed record TransferStatusResponse(
        [property: JsonPropertyName("txHash")] string? TxHash,
        [property: JsonPropertyName("blockNumber")] long? BlockNumber,
        [property: JsonPropertyName("contractRet")] string? ContractRet,
        [property: JsonPropertyName("confirmations")] int? Confirmations,
        [property: JsonPropertyName("finalized")] bool? Finalized = null,
        [property: JsonPropertyName("realizedFeeSun")] long? RealizedFeeSun = null,
        [property: JsonPropertyName("energyUsageTotal")] long? EnergyUsageTotal = null,
        [property: JsonPropertyName("originEnergyUsage")] long? OriginEnergyUsage = null,
        // T154 — Ethereum; a decimal string (sidecar-ethereum/contracts/transfer-status/).
        // Read as an element so a wrong JSON type is a logged NULL, not an unreadable status.
        [property: JsonPropertyName("realizedFeeWei")] JsonElement? RealizedFeeWei = null,
        // T160c düzeltme turu — Ethereum decimal string; both networks' price as a decimal string.
        [property: JsonPropertyName("platformSpendWei")] JsonElement? PlatformSpendWei = null,
        [property: JsonPropertyName("nativePriceUsdt")] JsonElement? NativePriceUsdt = null);

    private sealed record ErrorEnvelope(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("retryable")] bool? Retryable,
        [property: JsonPropertyName("deferred")] bool? Deferred = null,
        [property: JsonPropertyName("waitingSince")] string? WaitingSince = null);
}
