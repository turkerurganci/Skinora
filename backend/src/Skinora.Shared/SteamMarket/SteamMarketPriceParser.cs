using System.Globalization;
using System.Text.Json;

namespace Skinora.Shared.SteamMarket;

/// <summary>
/// Pure parsing helpers for the Steam Market <c>priceoverview</c> JSON
/// response (08 §7.2). Kept dependency-free so unit tests can exercise
/// every fiyat parse edge case without an HTTP fake.
/// </summary>
/// <remarks>
/// 08 §7.2 prescribes a fixed (not locale-aware) parse rule for USD
/// responses: strip the currency symbol, drop the thousands separator
/// (<c>,</c>), use <c>.</c> as the decimal separator. The official
/// response format is something like <c>"$12.50"</c> or
/// <c>"$1,234.56"</c>; this parser is deliberately permissive — any
/// non-digit/non-dot character is treated as decoration so a future
/// currency switch (06 §3.24 "Sabitler" tablo, 08 §7.5 büyüme yolu) is
/// a one-config-line change rather than a parser rewrite.
/// </remarks>
public static class SteamMarketPriceParser
{
    /// <summary>
    /// Parse a raw Steam Market price token (e.g. <c>"$12.50"</c>) into
    /// a decimal. Returns <c>null</c> when the input is missing, empty
    /// or unparseable per 08 §7.2 fallback chain.
    /// </summary>
    public static decimal? TryParsePrice(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        Span<char> buffer = stackalloc char[raw.Length];
        var written = 0;
        foreach (var c in raw)
        {
            if (char.IsDigit(c) || c == '.')
            {
                buffer[written++] = c;
            }
        }

        if (written == 0)
        {
            return null;
        }

        var cleaned = new string(buffer[..written]);
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Project a parsed <c>priceoverview</c> JSON document onto the
    /// 08 §7.2 fallback chain: median → lowest → no-price. Throws
    /// <see cref="SteamMarketPermanentException"/> when the payload
    /// reports <c>success: false</c> (item not listed, deprecated, etc.)
    /// — the orchestrator maps that to "skip fraud check + log" the
    /// same way it handles a no-price row, but the distinct exception
    /// preserves diagnostic signal.
    /// </summary>
    public static SteamMarketPriceQuote ParseResponse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new SteamMarketPermanentException("priceoverview payload was not a JSON object");
        }

        if (root.TryGetProperty("success", out var successElement)
            && successElement.ValueKind == JsonValueKind.False)
        {
            throw new SteamMarketPermanentException("priceoverview returned success=false");
        }

        var median = root.TryGetProperty("median_price", out var medianEl) && medianEl.ValueKind == JsonValueKind.String
            ? TryParsePrice(medianEl.GetString())
            : null;

        var lowest = root.TryGetProperty("lowest_price", out var lowestEl) && lowestEl.ValueKind == JsonValueKind.String
            ? TryParsePrice(lowestEl.GetString())
            : null;

        // 08 §7.2 priority chain — median > lowest > no-price.
        if (median.HasValue)
        {
            return SteamMarketPriceQuote.Median(median.Value, lowest);
        }

        if (lowest.HasValue)
        {
            return SteamMarketPriceQuote.Lowest(lowest.Value);
        }

        return SteamMarketPriceQuote.NoPrice();
    }
}
