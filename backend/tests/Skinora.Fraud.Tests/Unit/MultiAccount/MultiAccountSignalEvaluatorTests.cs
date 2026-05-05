using Skinora.Fraud.Application.MultiAccount;
using Skinora.Users.Application.MultiAccount;

namespace Skinora.Fraud.Tests.Unit.MultiAccount;

/// <summary>
/// Unit coverage for the pure helpers used by <see cref="MultiAccountDetector"/>
/// (T56 — 02 §14.3, 03 §7.4). Keeps the matching arithmetic out of the
/// integration suite so regressions surface in &lt;200ms.
/// </summary>
public class MultiAccountSignalEvaluatorTests
{
    // ---------- ParseExchangeAddresses ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseExchangeAddresses_Empty_Or_Null_Returns_Empty_Set(string? raw)
    {
        var result = MultiAccountSignalEvaluator.ParseExchangeAddresses(raw);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("NONE")]
    [InlineData(" NONE ")]
    public void ParseExchangeAddresses_None_Marker_Returns_Empty_Set(string raw)
    {
        var result = MultiAccountSignalEvaluator.ParseExchangeAddresses(raw);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseExchangeAddresses_Single_Address_Round_Trips()
    {
        var result = MultiAccountSignalEvaluator.ParseExchangeAddresses("TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL");
        Assert.Single(result);
        Assert.Contains("TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL", result);
    }

    [Fact]
    public void ParseExchangeAddresses_Csv_Splits_And_Trims_Each_Token()
    {
        var result = MultiAccountSignalEvaluator.ParseExchangeAddresses(
            "TAddr1, TAddr2 ,  TAddr3  ,TAddr4");
        Assert.Equal(4, result.Count);
        Assert.Contains("TAddr1", result);
        Assert.Contains("TAddr2", result);
        Assert.Contains("TAddr3", result);
        Assert.Contains("TAddr4", result);
    }

    [Fact]
    public void ParseExchangeAddresses_Drops_Empty_Tokens_From_Trailing_Comma()
    {
        var result = MultiAccountSignalEvaluator.ParseExchangeAddresses("TAddr1,,TAddr2,");
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseExchangeAddresses_Deduplicates_Identical_Tokens()
    {
        var result = MultiAccountSignalEvaluator.ParseExchangeAddresses("TAddr1,TAddr1,TAddr1");
        Assert.Single(result);
    }

    [Fact]
    public void ParseExchangeAddresses_Is_Case_Sensitive()
    {
        // TRC-20 addresses are case-mixed Base58; "TAddr1" and "taddr1" are
        // never interchangeable. Verify the parser does not collapse them.
        var result = MultiAccountSignalEvaluator.ParseExchangeAddresses("TAddr1,taddr1");
        Assert.Equal(2, result.Count);
    }

    // ---------- PickStrongMatchType ----------

    [Fact]
    public void PickStrongMatchType_No_Matches_Returns_Null()
    {
        Assert.Null(MultiAccountSignalEvaluator.PickStrongMatchType(false, false));
    }

    [Fact]
    public void PickStrongMatchType_Payout_Only_Returns_Payout()
    {
        Assert.Equal(
            MultiAccountMatchType.WALLET_PAYOUT,
            MultiAccountSignalEvaluator.PickStrongMatchType(hasPayoutMatch: true, hasRefundMatch: false));
    }

    [Fact]
    public void PickStrongMatchType_Refund_Only_Returns_Refund()
    {
        Assert.Equal(
            MultiAccountMatchType.WALLET_REFUND,
            MultiAccountSignalEvaluator.PickStrongMatchType(hasPayoutMatch: false, hasRefundMatch: true));
    }

    [Fact]
    public void PickStrongMatchType_Both_Match_Prefers_Payout()
    {
        Assert.Equal(
            MultiAccountMatchType.WALLET_PAYOUT,
            MultiAccountSignalEvaluator.PickStrongMatchType(hasPayoutMatch: true, hasRefundMatch: true));
    }
}
