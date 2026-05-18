using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skinora.Shared.SteamMarket;

/// <summary>
/// Raw <see cref="HttpClient"/> wrapper for Steam Market
/// <c>priceoverview</c> (08 §7.2). Mirrors the T78 / T79 / T80 transport
/// paterni — no third-party SDK, no implicit retry — so the cache
/// orchestrator above and the rate limiter behind it stay in charge of
/// policy.
/// </summary>
public sealed class SteamMarketPriceClient : ISteamMarketPriceClient
{
    private readonly HttpClient _http;
    private readonly ISteamMarketRateLimiter _rateLimiter;
    private readonly SteamMarketSettings _settings;
    private readonly ILogger<SteamMarketPriceClient> _logger;

    public SteamMarketPriceClient(
        HttpClient http,
        ISteamMarketRateLimiter rateLimiter,
        IOptions<SteamMarketSettings> settings,
        ILogger<SteamMarketPriceClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            throw new InvalidOperationException("SteamMarket:BaseUrl is required when provider=steam-market.");
        }

        _http.BaseAddress ??= new Uri(_settings.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
    }

    public async Task<SteamMarketPriceQuote> GetPriceAsync(string marketHashName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(marketHashName))
        {
            throw new ArgumentException("marketHashName is required.", nameof(marketHashName));
        }

        await _rateLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false);

        var path = "/market/priceoverview/"
            + $"?appid={_settings.AppId.ToString(CultureInfo.InvariantCulture)}"
            + $"&currency={_settings.Currency.ToString(CultureInfo.InvariantCulture)}"
            + $"&market_hash_name={Uri.EscapeDataString(marketHashName)}";

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SteamMarketTransientException("Steam Market request timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SteamMarketTransientException("Steam Market transport failure.", ex);
        }

        using var _ = response;

        if (response.StatusCode == (HttpStatusCode)429)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date.HasValue == true
                    ? response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow
                    : (TimeSpan?)null);

            _rateLimiter.RegisterRetryAfter(retryAfter ?? TimeSpan.FromSeconds(30));
            throw new SteamMarketRateLimitedException(
                $"Steam Market returned 429 (retry-after: {retryAfter?.ToString() ?? "unspecified"}).",
                retryAfter);
        }

        if ((int)response.StatusCode >= 500)
        {
            throw new SteamMarketTransientException(
                $"Steam Market returned {(int)response.StatusCode}.",
                statusCode: (int)response.StatusCode);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new SteamMarketPermanentException(
                $"Steam Market returned {(int)response.StatusCode}.",
                statusCode: (int)response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new SteamMarketPermanentException(
                "priceoverview body was not valid JSON.",
                ex,
                statusCode: (int)response.StatusCode);
        }

        using (document)
        {
            try
            {
                var quote = SteamMarketPriceParser.ParseResponse(document.RootElement);
                if (quote.IsNoPrice)
                {
                    _logger.LogInformation(
                        "Steam Market priceoverview returned no price for {MarketHashName}.",
                        marketHashName);
                }
                return quote;
            }
            catch (SteamMarketPermanentException ex) when (ex.Message.Contains("success=false", StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Steam Market priceoverview reported success=false for {MarketHashName}.",
                    marketHashName);
                return SteamMarketPriceQuote.NoPrice();
            }
        }
    }
}
