namespace Skinora.Shared.SteamMarket;

/// <summary>
/// Marker base for Steam Market transport failures (08 §7.4). The cache
/// orchestrator (Skinora.Fraud PriceService) maps these to "fiyat
/// kontrolü atlanır" per the spec without surfacing as a transaction
/// failure to the user.
/// </summary>
public abstract class SteamMarketException : Exception
{
    protected SteamMarketException(string message) : base(message) { }

    protected SteamMarketException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Transient transport failure — 5xx, timeout, network glitch (08 §7.4
/// "API erişilemez" satırı). PriceService responds by leaving the cache
/// untouched and the caller skips the fraud check + logs.
/// </summary>
public sealed class SteamMarketTransientException : SteamMarketException
{
    public int? StatusCode { get; }

    public SteamMarketTransientException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public SteamMarketTransientException(string message, Exception inner, int? statusCode = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Rate-limit response from Steam Market — 429 or implicit throttling
/// after the local sliding-window cap has been honoured (08 §7.4 "Rate
/// limit" satırı). PriceService treats this like a transient and skips
/// the fraud check; the limiter already paused the next call.
/// </summary>
public sealed class SteamMarketRateLimitedException : SteamMarketException
{
    public TimeSpan? RetryAfter { get; }

    public SteamMarketRateLimitedException(string message, TimeSpan? retryAfter = null)
        : base(message)
    {
        RetryAfter = retryAfter;
    }
}

/// <summary>
/// Permanent failure — Steam returned a non-success payload, malformed
/// response, or 4xx that is not a rate limit (08 §7.4 "Item cache'te yok
/// + API'den alınamıyor" satırı maps to <see cref="SteamMarketPriceQuote.NoPrice"/>
/// via the parser, but other 4xx surfaces here).
/// </summary>
public sealed class SteamMarketPermanentException : SteamMarketException
{
    public int? StatusCode { get; }

    public SteamMarketPermanentException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public SteamMarketPermanentException(string message, Exception inner, int? statusCode = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
