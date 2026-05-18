using Skinora.Shared.Telegram;
using Xunit;

namespace Skinora.Shared.Tests.Unit.Telegram;

public class MarkdownV2EscaperTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("plain ASCII text", "plain ASCII text")]
    public void Escape_NoReservedChars_ReturnsInputUnchanged(string? input, string expected)
    {
        Assert.Equal(expected, MarkdownV2Escaper.Escape(input));
    }

    [Theory]
    [InlineData("_", "\\_")]
    [InlineData("*", "\\*")]
    [InlineData("[", "\\[")]
    [InlineData("]", "\\]")]
    [InlineData("(", "\\(")]
    [InlineData(")", "\\)")]
    [InlineData("~", "\\~")]
    [InlineData("`", "\\`")]
    [InlineData(">", "\\>")]
    [InlineData("#", "\\#")]
    [InlineData("+", "\\+")]
    [InlineData("-", "\\-")]
    [InlineData("=", "\\=")]
    [InlineData("|", "\\|")]
    [InlineData("{", "\\{")]
    [InlineData("}", "\\}")]
    [InlineData(".", "\\.")]
    [InlineData("!", "\\!")]
    [InlineData("\\", "\\\\")]
    public void Escape_SingleReservedChar_PrependsBackslash(string input, string expected)
    {
        Assert.Equal(expected, MarkdownV2Escaper.Escape(input));
    }

    [Fact]
    public void Escape_TransactionMessage_EscapesAllReservedChars()
    {
        // 08 §5.2 — a typical transaction notification body. Item name
        // contains '.' and '|', amount contains '.', URL contains '(', ')'.
        var input = "AK-47 | Redline (Field-Tested) 12.50 USDT";
        var expected = "AK\\-47 \\| Redline \\(Field\\-Tested\\) 12\\.50 USDT";

        Assert.Equal(expected, MarkdownV2Escaper.Escape(input));
    }

    [Fact]
    public void Escape_AlreadyEscapedString_DoubleEscapesBackslashes()
    {
        // If a caller hands us pre-escaped input we still escape — this
        // is intentional, the escaper is allow-list-free.
        Assert.Equal("\\\\\\.", MarkdownV2Escaper.Escape("\\."));
    }
}
