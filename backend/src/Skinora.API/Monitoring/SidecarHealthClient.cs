using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;
using Skinora.Steam.Application.Inventory;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.API.Monitoring;

/// <summary>
/// Default <see cref="ISidecarHealthClient"/> — issues a short GET to each
/// sidecar's <c>/health</c> endpoint, carrying the same <c>X-Internal-Key</c>
/// the other sidecar clients use (05 §3.4) so the probe works whether or not the
/// sidecar gates <c>/health</c> behind the internal-key middleware.
/// </summary>
/// <remarks>
/// T148 — every network's blockchain service is its own component, probed at
/// its own address (05 §3.3a "Sağlık ve dondurma"). When the service names its
/// network in the <c>/health</c> body (<c>"network": "TRON"</c>) and that is
/// not the network the component stands for, the address points at the wrong
/// service: the probe reports it unhealthy rather than let one network's
/// health stand in for another's.
/// </remarks>
public sealed class SidecarHealthClient : ISidecarHealthClient
{
    /// <summary>Named <see cref="HttpClient"/> for sidecar health probes.</summary>
    public const string HttpClientName = "PlatformHealthProbe";

    private const string InternalKeyHeader = "X-Internal-Key";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SteamSidecarOptions _steam;
    private readonly IOptionsMonitor<BlockchainSidecarOptions> _blockchain;
    private readonly ILogger<SidecarHealthClient> _logger;

    public SidecarHealthClient(
        IHttpClientFactory httpClientFactory,
        IOptions<SteamSidecarOptions> steam,
        IOptionsMonitor<BlockchainSidecarOptions> blockchain,
        ILogger<SidecarHealthClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _steam = steam.Value;
        _blockchain = blockchain;
        _logger = logger;
    }

    public async Task<bool?> IsHealthyAsync(string component, CancellationToken cancellationToken)
    {
        BlockchainNetwork? network = null;
        string baseUrl;
        string internalKey;
        if (component == PlatformComponents.Steam)
        {
            (baseUrl, internalKey) = (_steam.BaseUrl, _steam.InternalKey);
        }
        else if (PlatformComponents.TryGetNetwork(component, out var resolved))
        {
            network = resolved;
            var options = _blockchain.Get(BlockchainSidecarOptions.OptionsNameFor(resolved));
            (baseUrl, internalKey) = (options.BaseUrl, options.InternalKey);
        }
        else
        {
            (baseUrl, internalKey) = (string.Empty, string.Empty);
        }

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
            if (!response.IsSuccessStatusCode) return false;

            if (network is { } expected)
            {
                var reported = await ReadReportedNetworkAsync(response, cancellationToken);
                if (reported is not null
                    && !string.Equals(reported, expected.ToString(), StringComparison.Ordinal))
                {
                    _logger.LogError(
                        "Health probe for {Component} reached a service that reports network '{Reported}' — "
                        + "the {Expected} service address points at the wrong service. Reporting unhealthy.",
                        component, reported, expected);
                    return false;
                }
            }

            return true;
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

    /// <summary>
    /// The <c>network</c> field of a <c>/health</c> body, or <c>null</c> when the
    /// body has none or is not JSON — a service predating the field is not
    /// penalised for it.
    /// </summary>
    private static async Task<string?> ReadReportedNetworkAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("network", out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
