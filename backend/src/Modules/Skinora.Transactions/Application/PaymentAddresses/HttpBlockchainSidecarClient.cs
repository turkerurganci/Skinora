using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skinora.Transactions.Application.PaymentAddresses;

/// <summary>
/// <see cref="HttpClient"/>-backed implementation of
/// <see cref="IBlockchainSidecarClient"/>. Routes to the blockchain sidecar
/// container; service-to-service auth uses the shared
/// <c>X-Internal-Key</c> header (05 §3.4).
/// </summary>
public sealed class HttpBlockchainSidecarClient : IBlockchainSidecarClient
{
    /// <summary>HTTP client name used by <c>AddHttpClient</c>.</summary>
    public const string HttpClientName = "BlockchainSidecar";

    private const string InternalKeyHeader = "X-Internal-Key";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly BlockchainSidecarOptions _options;
    private readonly ILogger<HttpBlockchainSidecarClient> _logger;

    public HttpBlockchainSidecarClient(
        HttpClient http,
        IOptions<BlockchainSidecarOptions> options,
        ILogger<HttpBlockchainSidecarClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<BlockchainSidecarDeriveResult> DeriveAddressAsync(
        int index,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/wallet/derive")
        {
            Content = JsonContent.Create(
                new DeriveRequest(index, transactionId.ToString("D")),
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
            Address: request.Address,
            PaymentAddressId: request.PaymentAddressId.ToString("D"),
            TransactionId: request.TransactionId.ToString("D"),
            ExpectedContract: request.ExpectedContract,
            ExpectedSymbol: request.ExpectedSymbol);

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
        var body = new MonitorStopRequestBody(Address: address);
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
        var body = new PostCancelStopRequestBody(Address: address);
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
                new BalancesRequest(addresses),
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

    public async Task<BlockchainSidecarTransferResult> SendHotToColdTransferAsync(
        HotToColdTransferRequest request,
        CancellationToken cancellationToken)
    {
        var body = new ColdTransferRequestBody(
            ColdTransferId: request.ColdTransferId.ToString("D"),
            ToColdAddress: request.ToColdAddress,
            Amount: request.Amount,
            Token: request.Token);

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
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("transactionId")] string TransactionId);

    private sealed record DeriveResponse(
        [property: JsonPropertyName("address")] string? Address,
        [property: JsonPropertyName("derivationPath")] string? DerivationPath,
        [property: JsonPropertyName("index")] int Index);

    private sealed record MonitorStartRequestBody(
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("paymentAddressId")] string PaymentAddressId,
        [property: JsonPropertyName("transactionId")] string TransactionId,
        [property: JsonPropertyName("expectedContract")] string ExpectedContract,
        [property: JsonPropertyName("expectedSymbol")] string ExpectedSymbol);

    private sealed record MonitorStopRequestBody(
        [property: JsonPropertyName("address")] string Address);

    private sealed record PostCancelStartRequestBody(
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("paymentAddressId")] string PaymentAddressId,
        [property: JsonPropertyName("transactionId")] string TransactionId,
        [property: JsonPropertyName("expectedContract")] string ExpectedContract,
        [property: JsonPropertyName("expectedSymbol")] string ExpectedSymbol,
        [property: JsonPropertyName("cancelledAt")] string CancelledAt,
        [property: JsonPropertyName("initialState")] string? InitialState,
        [property: JsonPropertyName("initialStateExpiresAt")] string? InitialStateExpiresAt);

    private sealed record PostCancelStopRequestBody(
        [property: JsonPropertyName("address")] string Address);

    private sealed record BalancesRequest(
        [property: JsonPropertyName("addresses")] IReadOnlyList<string> Addresses);

    private sealed record BalancesResponse(
        [property: JsonPropertyName("blockNumber")] long BlockNumber,
        [property: JsonPropertyName("balances")] List<BalancesRow>? Balances);

    private sealed record BalancesRow(
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("tokens")] Dictionary<string, string>? Tokens);

    private sealed record ColdTransferRequestBody(
        [property: JsonPropertyName("coldTransferId")] string ColdTransferId,
        [property: JsonPropertyName("toColdAddress")] string ToColdAddress,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("token")] string Token);

    private sealed record ColdTransferResponse(
        [property: JsonPropertyName("txHash")] string? TxHash);
}
