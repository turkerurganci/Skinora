using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.PaymentAddresses;

/// <summary>
/// <see cref="HttpClient"/>-backed implementation of
/// <see cref="IBlockchainSidecarClient"/> for ONE network's blockchain service
/// (T148 — 05 §3.3a). Service-to-service auth uses that service's own
/// <c>X-Internal-Key</c> (05 §3.4), and every request body names the network
/// (<c>"network": "TRON"</c>) so a service reached at the wrong address
/// refuses the call instead of acting on it.
/// </summary>
/// <remarks>
/// Instances are built by <c>BlockchainServiceRegistry</c> per network; callers
/// never hold one directly — they ask the registry with the network of the
/// record they act on (<c>PaymentAddress.Network</c>, <c>BlockchainTransaction.Network</c>).
/// </remarks>
public sealed class HttpBlockchainSidecarClient : IBlockchainSidecarClient
{
    /// <summary>TRON's named HTTP client (pre-F8 name, kept); see <see cref="Networks.BlockchainHttpClientNames"/>.</summary>
    public const string HttpClientName = "BlockchainSidecar";

    private const string InternalKeyHeader = "X-Internal-Key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BlockchainNetwork _network;
    private readonly string _networkWire;
    private readonly HttpClient _http;
    private readonly BlockchainSidecarOptions _options;
    private readonly ILogger<HttpBlockchainSidecarClient> _logger;

    public HttpBlockchainSidecarClient(
        BlockchainNetwork network,
        HttpClient http,
        BlockchainSidecarOptions options,
        ILogger<HttpBlockchainSidecarClient> logger)
    {
        _network = network;
        _networkWire = network.ToString();
        _http = http;
        _options = options;
        _logger = logger;
    }

    /// <summary>The network whose service this client talks to.</summary>
    public BlockchainNetwork Network => _network;

    public async Task<BlockchainSidecarDeriveResult> DeriveAddressAsync(
        int index,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/wallet/derive")
        {
            Content = JsonContent.Create(
                new DeriveRequest(_networkWire, index, transactionId.ToString("D")),
                options: JsonOptions),
        };
        if (!string.IsNullOrEmpty(_options.InternalKey))
        {
            request.Headers.TryAddWithoutValidation(InternalKeyHeader, _options.InternalKey);
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "Blockchain sidecar derive request failed for transaction {TransactionId} index {Index}",
                transactionId, index);
            return new BlockchainSidecarDeriveResult(BlockchainSidecarStatus.Unavailable, null, null);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                _logger.LogError(
                    "Blockchain sidecar reports HD wallet not configured (transaction {TransactionId})",
                    transactionId);
                return new BlockchainSidecarDeriveResult(
                    BlockchainSidecarStatus.NotConfigured, null, null);
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                _logger.LogWarning(
                    "Blockchain sidecar rejected derive request for transaction {TransactionId} index {Index}",
                    transactionId, index);
                return new BlockchainSidecarDeriveResult(
                    BlockchainSidecarStatus.InvalidRequest, null, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Blockchain sidecar derive returned {StatusCode} for transaction {TransactionId}",
                    (int)response.StatusCode, transactionId);
                return new BlockchainSidecarDeriveResult(
                    BlockchainSidecarStatus.Unavailable, null, null);
            }

            DeriveResponse? payload;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<DeriveResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Blockchain sidecar derive payload unparsable for transaction {TransactionId}",
                    transactionId);
                return new BlockchainSidecarDeriveResult(
                    BlockchainSidecarStatus.Unavailable, null, null);
            }

            if (payload is null
                || string.IsNullOrWhiteSpace(payload.Address)
                || string.IsNullOrWhiteSpace(payload.DerivationPath))
            {
                _logger.LogWarning(
                    "Blockchain sidecar derive returned empty payload for transaction {TransactionId}",
                    transactionId);
                return new BlockchainSidecarDeriveResult(
                    BlockchainSidecarStatus.Unavailable, null, null);
            }

            return new BlockchainSidecarDeriveResult(
                BlockchainSidecarStatus.Success, payload.Address, payload.DerivationPath);
        }
    }

    public async Task<BlockchainSidecarStatus> StartMonitoringAsync(
        PaymentMonitorStartRequest request,
        CancellationToken cancellationToken)
    {
        var body = new MonitorStartRequestBody(
            Network: _networkWire,
            Address: request.Address,
            PaymentAddressId: request.PaymentAddressId.ToString("D"),
            TransactionId: request.TransactionId.ToString("D"),
            ExpectedContract: request.ExpectedContract,
            ExpectedSymbol: request.ExpectedSymbol,
            Cadence: ToWire(request.Cadence));

        return await SendCommandAsync(
            "api/monitor/start",
            body,
            logContext: $"address={request.Address} transactionId={request.TransactionId}",
            cancellationToken);
    }

    public async Task<BlockchainSidecarStatus> StopMonitoringAsync(
        string address,
        CancellationToken cancellationToken)
    {
        var body = new MonitorStopRequestBody(Network: _networkWire, Address: address);
        return await SendCommandAsync(
            "api/monitor/stop",
            body,
            logContext: $"address={address}",
            cancellationToken);
    }

    public async Task<BlockchainSidecarStatus> StartPostCancelMonitoringAsync(
        PostCancelMonitorStartRequest request,
        CancellationToken cancellationToken)
    {
        var body = new PostCancelStartRequestBody(
            Network: _networkWire,
            Address: request.Address,
            PaymentAddressId: request.PaymentAddressId.ToString("D"),
            TransactionId: request.TransactionId.ToString("D"),
            ExpectedContract: request.ExpectedContract,
            ExpectedSymbol: request.ExpectedSymbol,
            CancelledAt: request.CancelledAt.ToUniversalTime().ToString("O"),
            InitialState: request.InitialState,
            InitialStateExpiresAt: request.InitialStateExpiresAt?.ToUniversalTime().ToString("O"));

        return await SendCommandAsync(
            "api/monitor/post-cancel-start",
            body,
            logContext: $"address={request.Address} transactionId={request.TransactionId}",
            cancellationToken);
    }

    public async Task<BlockchainSidecarStatus> StopPostCancelMonitoringAsync(
        string address,
        CancellationToken cancellationToken)
    {
        var body = new PostCancelStopRequestBody(Network: _networkWire, Address: address);
        return await SendCommandAsync(
            "api/monitor/post-cancel-stop",
            body,
            logContext: $"address={address}",
            cancellationToken);
    }

    public async Task<BlockchainSidecarBalancesResult> GetWalletBalancesAsync(
        IReadOnlyList<string> addresses,
        CancellationToken cancellationToken)
    {
        if (addresses.Count == 0)
        {
            return new BlockchainSidecarBalancesResult(
                BlockchainSidecarStatus.Success,
                BlockNumber: null,
                Balances: Array.Empty<BlockchainSidecarAddressBalances>());
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/wallet/balances")
        {
            Content = JsonContent.Create(
                new BalancesRequest(_networkWire, addresses),
                options: JsonOptions),
        };
        if (!string.IsNullOrEmpty(_options.InternalKey))
        {
            request.Headers.TryAddWithoutValidation(InternalKeyHeader, _options.InternalKey);
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "Blockchain sidecar balances request failed (addressCount={Count})",
                addresses.Count);
            return BlockchainSidecarBalancesResult.Unavailable;
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                _logger.LogWarning(
                    "Blockchain sidecar rejected balances payload (addressCount={Count})",
                    addresses.Count);
                return new BlockchainSidecarBalancesResult(
                    BlockchainSidecarStatus.InvalidRequest, null, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Blockchain sidecar balances returned {StatusCode} (addressCount={Count})",
                    (int)response.StatusCode, addresses.Count);
                return BlockchainSidecarBalancesResult.Unavailable;
            }

            BalancesResponse? payload;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<BalancesResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Blockchain sidecar balances payload unparsable (addressCount={Count})",
                    addresses.Count);
                return BlockchainSidecarBalancesResult.Unavailable;
            }

            if (payload is null || payload.Balances is null)
            {
                _logger.LogWarning(
                    "Blockchain sidecar balances returned empty payload (addressCount={Count})",
                    addresses.Count);
                return BlockchainSidecarBalancesResult.Unavailable;
            }

            var rows = new List<BlockchainSidecarAddressBalances>(payload.Balances.Count);
            foreach (var row in payload.Balances)
            {
                if (row is null || string.IsNullOrWhiteSpace(row.Address)) continue;
                var tokens = row.Tokens ?? new Dictionary<string, string>(StringComparer.Ordinal);
                rows.Add(new BlockchainSidecarAddressBalances(row.Address, tokens));
            }

            return new BlockchainSidecarBalancesResult(
                BlockchainSidecarStatus.Success, payload.BlockNumber, rows);
        }
    }

    public async Task<BlockchainSidecarPlatformWalletsResult> GetPlatformWalletsAsync(
        CancellationToken cancellationToken)
    {
        // Only the Ethereum service has this call (T157); asking the TRON
        // service would be a 404 for a question it has no answer to.
        if (_network != BlockchainNetwork.ETHEREUM)
        {
            return BlockchainSidecarPlatformWalletsResult.NotAsked;
        }

        using var http = new HttpRequestMessage(HttpMethod.Post, "api/wallet/platform")
        {
            Content = JsonContent.Create(new PlatformWalletsRequestBody(_networkWire), options: JsonOptions),
        };
        if (!string.IsNullOrEmpty(_options.InternalKey))
        {
            http.Headers.TryAddWithoutValidation(InternalKeyHeader, _options.InternalKey);
        }
        http.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await _http.SendAsync(http, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Blockchain sidecar platform wallets returned {StatusCode}", (int)response.StatusCode);
                return BlockchainSidecarPlatformWalletsResult.Unavailable;
            }

            var payload = await response.Content
                .ReadFromJsonAsync<PlatformWalletsResponse>(JsonOptions, cancellationToken);
            return payload is null
                ? BlockchainSidecarPlatformWalletsResult.Unavailable
                : new BlockchainSidecarPlatformWalletsResult(
                    BlockchainSidecarStatus.Success,
                    payload.HotWalletAddress,
                    payload.FuelWalletAddress,
                    payload.ColdWalletAddress);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Blockchain sidecar platform wallets request failed");
            return BlockchainSidecarPlatformWalletsResult.Unavailable;
        }
    }

    public async Task<BlockchainSidecarTransferResult> SendHotToColdTransferAsync(
        HotToColdTransferRequest request,
        CancellationToken cancellationToken)
    {
        var body = new ColdTransferRequestBody(
            Network: _networkWire,
            ColdTransferId: request.ColdTransferId.ToString("D"),
            ToColdAddress: request.ToColdAddress,
            Amount: request.Amount,
            Token: request.Token,
            FeeCapUsd: request.FeeCapUsd);

        using var http = new HttpRequestMessage(HttpMethod.Post, "api/transfer/cold-wallet")
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        if (!string.IsNullOrEmpty(_options.InternalKey))
        {
            http.Headers.TryAddWithoutValidation(InternalKeyHeader, _options.InternalKey);
        }
        http.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(http, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "Blockchain sidecar cold-wallet transfer request failed (coldTransferId={Id}, token={Token})",
                request.ColdTransferId, request.Token);
            return new BlockchainSidecarTransferResult(
                BlockchainSidecarStatus.Unavailable, null);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                _logger.LogWarning(
                    "Blockchain sidecar rejected cold-wallet transfer (coldTransferId={Id}, token={Token})",
                    request.ColdTransferId, request.Token);
                return new BlockchainSidecarTransferResult(
                    BlockchainSidecarStatus.InvalidRequest, null);
            }

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                // T157 — the Ethereum service answers 503 for "not signed, for
                // now" (fee above the cap, no ETH price, no gas — `deferred`)
                // and for a stopped account (`JOURNAL_BEHIND_CHAIN`); nothing
                // was signed. Any other 503 is the TRON meaning: the hot wallet
                // is not configured.
                var deferredReason = await TryReadDeferredReasonAsync(response, cancellationToken);
                if (deferredReason is not null)
                {
                    _logger.LogWarning(
                        "Blockchain sidecar did not sign the cold-wallet transfer for now: {Reason} (coldTransferId={Id})",
                        deferredReason, request.ColdTransferId);
                    return new BlockchainSidecarTransferResult(
                        BlockchainSidecarStatus.Deferred, null, deferredReason);
                }

                _logger.LogWarning(
                    "Blockchain sidecar reports hot wallet not configured for cold-wallet transfer (coldTransferId={Id})",
                    request.ColdTransferId);
                return new BlockchainSidecarTransferResult(
                    BlockchainSidecarStatus.NotConfigured, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Blockchain sidecar cold-wallet transfer returned {StatusCode} (coldTransferId={Id})",
                    (int)response.StatusCode, request.ColdTransferId);
                return new BlockchainSidecarTransferResult(
                    BlockchainSidecarStatus.Unavailable, null);
            }

            ColdTransferResponse? payload;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<ColdTransferResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Blockchain sidecar cold-wallet transfer payload unparsable (coldTransferId={Id})",
                    request.ColdTransferId);
                return new BlockchainSidecarTransferResult(
                    BlockchainSidecarStatus.Unavailable, null);
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.TxHash))
            {
                _logger.LogWarning(
                    "Blockchain sidecar cold-wallet transfer returned empty txHash (coldTransferId={Id})",
                    request.ColdTransferId);
                return new BlockchainSidecarTransferResult(
                    BlockchainSidecarStatus.Unavailable, null);
            }

            return new BlockchainSidecarTransferResult(
                BlockchainSidecarStatus.Success, payload.TxHash);
        }
    }

    private async Task<BlockchainSidecarStatus> SendCommandAsync<TBody>(
        string path,
        TBody body,
        string logContext,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        if (!string.IsNullOrEmpty(_options.InternalKey))
        {
            request.Headers.TryAddWithoutValidation(InternalKeyHeader, _options.InternalKey);
        }
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "Blockchain sidecar {Path} failed ({Context})", path, logContext);
            return BlockchainSidecarStatus.Unavailable;
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return BlockchainSidecarStatus.Success;
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                _logger.LogWarning(
                    "Blockchain sidecar {Path} rejected payload ({Context})", path, logContext);
                return BlockchainSidecarStatus.InvalidRequest;
            }

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                _logger.LogWarning(
                    "Blockchain sidecar {Path} reports not configured ({Context})", path, logContext);
                return BlockchainSidecarStatus.NotConfigured;
            }

            _logger.LogWarning(
                "Blockchain sidecar {Path} returned {StatusCode} ({Context})",
                path, (int)response.StatusCode, logContext);
            return BlockchainSidecarStatus.Unavailable;
        }
    }

    private sealed record DeriveRequest(
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("transactionId")] string TransactionId);

    private sealed record DeriveResponse(
        [property: JsonPropertyName("address")] string? Address,
        [property: JsonPropertyName("derivationPath")] string? DerivationPath,
        [property: JsonPropertyName("index")] int Index);

    private sealed record MonitorStartRequestBody(
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("paymentAddressId")] string PaymentAddressId,
        [property: JsonPropertyName("transactionId")] string TransactionId,
        [property: JsonPropertyName("expectedContract")] string ExpectedContract,
        [property: JsonPropertyName("expectedSymbol")] string ExpectedSymbol,
        [property: JsonPropertyName("cadence")] string Cadence);

    /// <summary>
    /// The sidecar's spelling (monitorHandlers.ts ALLOWED_CADENCES). An unknown
    /// value throws rather than defaulting: the sidecar rejects anything else
    /// with 400, and a silent default would pick one of the two costs the
    /// field exists to choose between.
    /// </summary>
    private static string ToWire(PaymentMonitorCadence cadence) => cadence switch
    {
        PaymentMonitorCadence.Payment => "PAYMENT",
        PaymentMonitorCadence.Holding => "HOLDING",
        _ => throw new ArgumentOutOfRangeException(nameof(cadence), cadence, "Unknown monitor cadence."),
    };

    private sealed record MonitorStopRequestBody(
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("address")] string Address);

    private sealed record PostCancelStartRequestBody(
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("paymentAddressId")] string PaymentAddressId,
        [property: JsonPropertyName("transactionId")] string TransactionId,
        [property: JsonPropertyName("expectedContract")] string ExpectedContract,
        [property: JsonPropertyName("expectedSymbol")] string ExpectedSymbol,
        [property: JsonPropertyName("cancelledAt")] string CancelledAt,
        [property: JsonPropertyName("initialState")] string? InitialState,
        [property: JsonPropertyName("initialStateExpiresAt")] string? InitialStateExpiresAt);

    private sealed record PostCancelStopRequestBody(
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("address")] string Address);

    private sealed record BalancesRequest(
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("addresses")] IReadOnlyList<string> Addresses);

    private sealed record BalancesResponse(
        [property: JsonPropertyName("blockNumber")] long BlockNumber,
        [property: JsonPropertyName("balances")] List<BalancesRow>? Balances);

    private sealed record BalancesRow(
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("tokens")] Dictionary<string, string>? Tokens);

    /// <summary>
    /// The reason code of a 503 that means "not signed for now" (T157), or
    /// <c>null</c> when the body says something else or cannot be read.
    /// </summary>
    private static async Task<string?> TryReadDeferredReasonAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content
                .ReadFromJsonAsync<DeferredBody>(JsonOptions, cancellationToken);
            if (body is null) return null;
            if (body.Deferred == true || string.Equals(body.Error, "JOURNAL_BEHIND_CHAIN", StringComparison.Ordinal))
            {
                return string.IsNullOrWhiteSpace(body.Error) ? "DEFERRED" : body.Error;
            }
            return null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record DeferredBody(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("deferred")] bool? Deferred);

    private sealed record ColdTransferRequestBody(
        [property: JsonPropertyName("network")] string Network,
        [property: JsonPropertyName("coldTransferId")] string ColdTransferId,
        [property: JsonPropertyName("toColdAddress")] string ToColdAddress,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("feeCapUsd"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FeeCapUsd);

    private sealed record PlatformWalletsRequestBody(
        [property: JsonPropertyName("network")] string Network);

    private sealed record PlatformWalletsResponse(
        [property: JsonPropertyName("hotWalletAddress")] string? HotWalletAddress,
        [property: JsonPropertyName("fuelWalletAddress")] string? FuelWalletAddress,
        [property: JsonPropertyName("coldWalletAddress")] string? ColdWalletAddress);

    private sealed record ColdTransferResponse(
        [property: JsonPropertyName("txHash")] string? TxHash);
}
