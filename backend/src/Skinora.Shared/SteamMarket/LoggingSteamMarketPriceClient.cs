using Microsoft.Extensions.Logging;

namespace Skinora.Shared.SteamMarket;

/// <summary>
/// Fail-closed stub used when <see cref="SteamMarketSettings.Provider"/>
/// is <c>logging</c> (default — CI / fresh checkout). Reports a
/// no-price quote for every call so the fraud pipeline degrades to
/// 08 §7.4 karar ağacı adım 3b ("fiyat kontrolü atla + log") without
/// any outbound traffic.
/// </summary>
public sealed class LoggingSteamMarketPriceClient : ISteamMarketPriceClient
{
    private readonly ILogger<LoggingSteamMarketPriceClient> _logger;

    public LoggingSteamMarketPriceClient(ILogger<LoggingSteamMarketPriceClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SteamMarketPriceQuote> GetPriceAsync(string marketHashName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(marketHashName))
        {
            throw new ArgumentException("marketHashName is required.", nameof(marketHashName));
        }

        _logger.LogDebug(
            "SteamMarket provider=logging — returning no-price stub for {MarketHashName}.",
            marketHashName);
        return Task.FromResult(SteamMarketPriceQuote.NoPrice());
    }
}
