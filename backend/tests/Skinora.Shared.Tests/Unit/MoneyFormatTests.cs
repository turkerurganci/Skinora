using System.Globalization;
using Skinora.Shared.Domain;

namespace Skinora.Shared.Tests.Unit;

/// <summary>
/// T144 — the text form of a USDT amount a user reads (07 §2.8, 09 §7.2):
/// at least two decimals, never rounded.
/// </summary>
public class MoneyFormatTests
{
    [Theory]
    [InlineData("10.76", "10.76")]
    [InlineData("102", "102.00")]
    [InlineData("10.5", "10.50")]
    [InlineData("0", "0.00")]
    [InlineData("0.01", "0.01")]
    // Values read back from decimal(18,6) carry trailing zeros.
    [InlineData("10.760000", "10.76")]
    [InlineData("99999999.990000", "99999999.99")]
    public void WholeCentAmounts_UseTheTwoDecimalForm(string value, string expected)
    {
        Assert.Equal(expected, MoneyFormat.Display(decimal.Parse(value, CultureInfo.InvariantCulture)));
    }

    [Theory]
    // An on-chain amount with a third digit is shown as received — the old
    // "0.00" display would have turned 10.761 into "10.76", the T144 defect.
    [InlineData("10.761", "10.761")]
    [InlineData("10.759999", "10.759999")]
    [InlineData("0.000001", "0.000001")]
    [InlineData("17.751234", "17.751234")]
    public void SubCentAmounts_AreNeverRounded(string value, string expected)
    {
        Assert.Equal(expected, MoneyFormat.Display(decimal.Parse(value, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void UsesInvariantDecimalPoint_RegardlessOfCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("1234.50", MoneyFormat.Display(1234.5m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
