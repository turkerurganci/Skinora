using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Steam.Application.Inventory;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.API.Monitoring;

/// <summary>
/// Default <see cref="ISidecarHealthClient"/> — issues a short GET to each
/// sidecar's <c>/health</c> endpoint, carrying the same <c>X-Internal-Key</c>
/// the other sidecar clients use (05 §3.4) so the probe works whether or not the
/// sidecar gates <c>/health</c> behind the internal-key middleware.
/// </summary>
public sealed class SidecarHealthClient : ISidecarHealthClient
{
    /// <summary>Named <see cref="HttpClient"/> for sidecar health probes.</summary>
    public const string HttpClientName = "PlatformHealthProbe";

    private const string InternalKeyHeader = "X-Internal-Key";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SteamSidecarOptions _steam;
    private readonly BlockchainSidecarOptions _blockchain;
    private readonly ILogger<SidecarHealthClient> _logger;

    public SidecarHealthClient(
        IHttpClientFactory httpClientFactory,
        IOptions<SteamSidecarOptions> steam,
        IOptions<BlockchainSidecarOptions> blockchain,
        ILogger<SidecarHealthClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _steam = steam.Value;
        _blockchain = blockchain.Value;
        _logger = logger;
    }

    public async Task<bool?> IsHealthyAsync(string component, CancellationToken cancellationToken)
    {
        var (baseUrl, internalKey) = component switch
        {
            PlatformComponents.Steam => (_steam.BaseUrl, _steam.InternalKey),
            PlatformComponents.Blockchain => (_blockchain.BaseUrl, _blockchain.InternalKey),
            _ => (string.Empty, string.Empty),
        };

        // Unconfigured component → not monitored (null), never a false outage.
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/health");
            if (!string.IsNullOrEmpty(internalKey))
                request.Headers.TryAddWithoutValidation(InternalKeyHeader, internalKey);

            using var response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // Connection refused / DNS / timeout — the sidecar is unreachable,
            // which is exactly the outage the probe exists to detect.
            _logger.LogWarning(
                ex, "Health probe to {Component} sidecar failed.", component);
            return false;
        }
    }
}
