using System.Globalization;
using System.Numerics;

namespace Skinora.Shared.Blockchain;

/// <summary>
/// Exact handling of Ethereum fee amounts in wei (T154 — 05 §3.3a "Tutar ve
/// ücret alanları", 06 §3.8 <c>RealizedFeeWei</c>, 08 §3.7.7).
/// </summary>
/// <remarks>
/// The Ethereum service sends every wei amount as a decimal <b>string</b>: a
/// fee passes 2^53 wei at ~0.009 ETH, so a JSON number read through a double
/// would round it. Here the string becomes a <see cref="decimal"/> (exact for
/// integers up to 28 digits — 10^28 wei is 10^10 ETH, beyond any fee) or a
/// <see cref="BigInteger"/>; no <c>double</c> is involved anywhere.
/// </remarks>
public static class WeiAmount
{
    /// <summary>The most digits a wei amount may have and still be held exactly by <see cref="decimal"/>.</summary>
    public const int MaxDigits = 28;

    private const int PriceScale = 6;

    /// <summary>
    /// Parses a wei amount: ASCII digits only, no sign, exponent, separator or
    /// leading zero (except <c>"0"</c>), at most <see cref="MaxDigits"/> digits.
    /// Anything else is refused — never read as zero, never rounded.
    /// </summary>
    public static bool TryParse(string? text, out decimal wei)
    {
        wei = 0m;
        if (!TryParseInteger(text, out var value)) return false;
        wei = (decimal)value;
        return true;
    }

    /// <summary>
    /// The USDT a fee of <paramref name="feeWei"/> costs at
    /// <paramref name="ethPriceUsdt"/> (USDT per ETH, at most 6 fraction
    /// digits), rounded <b>up</b> to cents — the same integer arithmetic as the
    /// Ethereum service (<c>sidecar-ethereum/src/fee/feeMath.ts</c>
    /// <c>weiToUsdtCentsCeil</c>), so both sides agree to the cent.
    /// </summary>
    public static bool TryUsdtCentsCeil(string? feeWei, string? ethPriceUsdt, out decimal feeUsdt)
    {
        feeUsdt = 0m;
        if (!TryParseInteger(feeWei, out var wei) || !TryParsePriceMicro(ethPriceUsdt, out var priceMicro))
            return false;
        // wei × micro-USDT / (10^18 wei/ETH × 10^4 micro-USDT/cent), rounded up.
        var denominator = BigInteger.Pow(10, 22);
        var cents = (wei * priceMicro + denominator - 1) / denominator;
        if (cents > new BigInteger(decimal.MaxValue)) return false;
        feeUsdt = (decimal)cents / 100m;
        return true;
    }

    private static bool TryParseInteger(string? text, out BigInteger value)
    {
        value = BigInteger.Zero;
        if (string.IsNullOrEmpty(text) || text.Length > MaxDigits) return false;
        foreach (var c in text)
        {
            if (c is < '0' or > '9') return false;
        }
        if (text.Length > 1 && text[0] == '0') return false;
        value = BigInteger.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        return true;
    }

    // "2414.579991" → 2414579991 (micro-USDT). Positive, ≤ 6 fraction digits.
    private static bool TryParsePriceMicro(string? text, out BigInteger micro)
    {
        micro = BigInteger.Zero;
        if (string.IsNullOrEmpty(text)) return false;
        var parts = text.Split('.');
        if (parts.Length > 2) return false;
        var whole = parts[0];
        var fraction = parts.Length == 2 ? parts[1] : string.Empty;
        if (whole.Length == 0 || (parts.Length == 2 && fraction.Length == 0) || fraction.Length > PriceScale)
            return false;
        foreach (var c in whole + fraction)
        {
            if (c is < '0' or > '9') return false;
        }
        micro = BigInteger.Parse(
            whole + fraction.PadRight(PriceScale, '0'), NumberStyles.None, CultureInfo.InvariantCulture);
        return micro > BigInteger.Zero;
    }
}
