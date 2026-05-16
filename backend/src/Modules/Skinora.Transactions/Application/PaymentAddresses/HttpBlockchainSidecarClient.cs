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

    private sealed record DeriveRequest(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("transactionId")] string TransactionId);

    private sealed record DeriveResponse(
        [property: JsonPropertyName("address")] string? Address,
        [property: JsonPropertyName("derivationPath")] string? DerivationPath,
        [property: JsonPropertyName("index")] int Index);
}
