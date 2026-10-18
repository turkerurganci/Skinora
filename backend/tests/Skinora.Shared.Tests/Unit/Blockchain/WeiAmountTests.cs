using System.Globalization;
using Skinora.Shared.Blockchain;

namespace Skinora.Shared.Tests.Unit.Blockchain;

/// <summary>
/// T154 — wei amounts reach the backend exactly (05 §3.3a). The arithmetic
/// must agree to the cent with the Ethereum service's
/// <c>sidecar-ethereum/src/fee/feeMath.ts</c> — the cases below are the same
/// numbers its tests use.
/// </summary>
public class WeiAmountTests
{
    [Theory]
    [InlineData("0", "0")]
    [InlineData("9007199254740993", "9007199254740993")] // 2^53 + 1: no double holds it
    [InlineData("9999999999999999999999999999", "9999999999999999999999999999")] // 28 digits
    public void TryParse_Reads_A_Decimal_String_Exactly(string text, string expected)
    {
        Assert.True(WeiAmount.TryParse(text, out var wei));
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), wei);
        Assert.Equal(expected, wei.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.0")]
    [InlineData("1e18")]
    [InlineData("0x10")]
    [InlineData("1,000")]
    [InlineData(" 1")]
    [InlineData("01")]
    [InlineData("٣")] // a non-ASCII digit
    [InlineData("99999999999999999999999999999")] // 29 digits
    public void TryParse_Refuses_Anything_But_Plain_Digits(string? text)
    {
        Assert.False(WeiAmount.TryParse(text, out var wei));
        Assert.Equal(0m, wei);
    }

    [Theory]
    // 2^53 + 1 wei at 2414.579991 → 21.748… → 21.75.
    [InlineData("9007199254740993", "2414.579991", "21.75")]
    // The contract example (sidecar-ethereum/contracts/estimate-fee/response.json):
    // 9,139,361,000,067,201 wei at 2414.579991 → 22.067… → 22.07.
    [InlineData("9139361000067201", "2414.579991", "22.07")]
    // T142: 125,482 gas × 0.217 gwei at 2,364.48 $ = 0.0643… → 0.07.
    [InlineData("27229594000000", "2364.48", "0.07")]
    // Exactly one cent stays one cent; one wei more is two.
    [InlineData("10000000000000", "1000", "0.01")]
    [InlineData("10000000000001", "1000", "0.02")]
    // 9,010,000,000,000,001 wei: a double drops the last wei and would charge 9.01.
    [InlineData("9010000000000001", "1000", "9.02")]
    [InlineData("1", "2414.579991", "0.01")]
    [InlineData("0", "2414.579991", "0")]
    public void TryUsdtCentsCeil_Matches_The_Services_Arithmetic(string wei, string price, string expected)
    {
        Assert.True(WeiAmount.TryUsdtCentsCeil(wei, price, out var usdt));
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), usdt);
    }

    [Theory]
    [InlineData("1000", null)]
    [InlineData("1000", "")]
    [InlineData("1000", "0")]
    [InlineData("1000", "0.000000")]
    [InlineData("1000", "-2400")]
    [InlineData("1000", "2400.1234567")] // more than 6 decimals — not the contract
    [InlineData("1000", "2.4e3")]
    [InlineData("1000", "2400.")]
    [InlineData("1000", ".5")]
    [InlineData("1000", "1.2.3")]
    [InlineData("1e3", "2400")]
    [InlineData(null, "2400")]
    public void TryUsdtCentsCeil_Refuses_A_Malformed_Fee_Or_Price(string? wei, string? price)
    {
        Assert.False(WeiAmount.TryUsdtCentsCeil(wei, price, out _));
    }
}
