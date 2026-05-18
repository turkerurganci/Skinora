using System.Text.Json;
using Skinora.Shared.SteamMarket;
using Xunit;

namespace Skinora.Shared.Tests.Unit.SteamMarket;

public class SteamMarketPriceParserTests
{
    // --- TryParsePrice ---

    [Theory]
    [InlineData("$12.50", 12.50)]
    [InlineData("$1,234.56", 1234.56)]
    [InlineData("$0.99", 0.99)]
    [InlineData("12.50", 12.50)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("$12", 12)]
    public void TryParsePrice_ValidUsdToken_ReturnsDecimal(string raw, decimal expected)
    {
        Assert.Equal(expected, SteamMarketPriceParser.TryParsePrice(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("$")]
    [InlineData("abc")]
    [InlineData("--")]
    public void TryParsePrice_EmptyOrSymbolOnly_ReturnsNull(string? raw)
    {
        Assert.Null(SteamMarketPriceParser.TryParsePrice(raw));
    }

    [Fact]
    public void TryParsePrice_StripsCurrencySymbolAndThousandsSeparator()
    {
        // 08 §7.2 fixed-format rule: locale-aware parsing is forbidden.
        // The parser must produce the same decimal whether the symbol is
        // before or after the digits, with or without thousands commas.
        Assert.Equal(1234.56m, SteamMarketPriceParser.TryParsePrice("$1,234.56"));
        Assert.Equal(1234.56m, SteamMarketPriceParser.TryParsePrice("1,234.56$"));
        Assert.Equal(1234.56m, SteamMarketPriceParser.TryParsePrice("USD 1,234.56"));
    }

    // --- ParseResponse fallback chain (08 §7.2) ---

    [Fact]
    public void ParseResponse_MedianAndLowestPresent_PrefersMedian()
    {
        var json = JsonDocument.Parse(
            "{\"success\":true,\"median_price\":\"$13.10\",\"lowest_price\":\"$12.50\"}");
        var quote = SteamMarketPriceParser.ParseResponse(json.RootElement);

        Assert.False(quote.IsNoPrice);
        Assert.Equal(13.10m, quote.MedianPrice);
        Assert.Equal(12.50m, quote.LowestPrice);
        Assert.Equal(13.10m, quote.EffectivePrice);
    }

    [Fact]
    public void ParseResponse_MedianMissing_FallsBackToLowest()
    {
        var json = JsonDocument.Parse(
            "{\"success\":true,\"lowest_price\":\"$12.50\"}");
        var quote = SteamMarketPriceParser.ParseResponse(json.RootElement);

        Assert.False(quote.IsNoPrice);
        Assert.Null(quote.MedianPrice);
        Assert.Equal(12.50m, quote.LowestPrice);
        Assert.Equal(12.50m, quote.EffectivePrice);
    }

    [Fact]
    public void ParseResponse_MedianUnparseable_FallsBackToLowest()
    {
        var json = JsonDocument.Parse(
            "{\"success\":true,\"median_price\":\"--\",\"lowest_price\":\"$12.50\"}");
        var quote = SteamMarketPriceParser.ParseResponse(json.RootElement);

        Assert.False(quote.IsNoPrice);
        Assert.Null(quote.MedianPrice);
        Assert.Equal(12.50m, quote.LowestPrice);
    }

    [Fact]
    public void ParseResponse_BothMissing_ReturnsNoPrice()
    {
        var json = JsonDocument.Parse("{\"success\":true}");
        var quote = SteamMarketPriceParser.ParseResponse(json.RootElement);

        Assert.True(quote.IsNoPrice);
        Assert.Null(quote.MedianPrice);
        Assert.Null(quote.LowestPrice);
        Assert.Null(quote.EffectivePrice);
    }

    [Fact]
    public void ParseResponse_BothEmptyStrings_ReturnsNoPrice()
    {
        var json = JsonDocument.Parse(
            "{\"success\":true,\"median_price\":\"\",\"lowest_price\":\"\"}");
        var quote = SteamMarketPriceParser.ParseResponse(json.RootElement);

        Assert.True(quote.IsNoPrice);
    }

    [Fact]
    public void ParseResponse_SuccessFalse_ThrowsPermanent()
    {
        var json = JsonDocument.Parse("{\"success\":false}");
        var ex = Assert.Throws<SteamMarketPermanentException>(() =>
            SteamMarketPriceParser.ParseResponse(json.RootElement));
        Assert.Contains("success=false", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseResponse_NotAnObject_ThrowsPermanent()
    {
        var json = JsonDocument.Parse("[]");
        Assert.Throws<SteamMarketPermanentException>(() =>
            SteamMarketPriceParser.ParseResponse(json.RootElement));
    }
}
