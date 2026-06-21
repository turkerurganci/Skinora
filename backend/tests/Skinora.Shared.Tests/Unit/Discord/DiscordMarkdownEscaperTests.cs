using Skinora.Shared.Discord;
using Xunit;

namespace Skinora.Shared.Tests.Unit.Discord;

public class DiscordMarkdownEscaperTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("plain ASCII text", "plain ASCII text")]
    public void Escape_NoReservedChars_ReturnsInputUnchanged(string? input, string expected)
    {
        Assert.Equal(expected, DiscordMarkdownEscaper.Escape(input));
    }

    [Theory]
    [InlineData("*", "\\*")]
    [InlineData("_", "\\_")]
    [InlineData("~", "\\~")]
    [InlineData("`", "\\`")]
    [InlineData(">", "\\>")]
    [InlineData("|", "\\|")]
    [InlineData("\\", "\\\\")]
    public void Escape_SingleReservedChar_PrependsBackslash(string input, string expected)
    {
        Assert.Equal(expected, DiscordMarkdownEscaper.Escape(input));
    }

    [Fact]
    public void Escape_DotAndBangNotReserved()
    {
        // Discord (unlike Telegram MarkdownV2) doesn't treat '.' or '!'
        // as formatting characters — confirming neither is escaped
        // keeps the parity test honest.
        Assert.Equal("Hello world.", DiscordMarkdownEscaper.Escape("Hello world."));
        Assert.Equal("Welcome!", DiscordMarkdownEscaper.Escape("Welcome!"));
    }

    [Fact]
    public void Escape_TransactionMessage_EscapesAllReservedChars()
    {
        // 08 §6.2 — a typical transaction notification body. Item name
        // contains '|', usernames could contain '_' and '*'.
        var input = "AK-47 | Redline 12.50 *USDT* received from _alice_";
        var expected = "AK-47 \\| Redline 12.50 \\*USDT\\* received from \\_alice\\_";

        Assert.Equal(expected, DiscordMarkdownEscaper.Escape(input));
    }

    [Fact]
    public void Escape_BlockQuoteLeadingChevron_Escaped()
    {
        // A stray '>' at the start of a line opens a Discord block
        // quote. The escaper neutralises it inline.
        Assert.Equal("\\> not a quote", DiscordMarkdownEscaper.Escape("> not a quote"));
    }

    [Fact]
    public void Escape_AlreadyEscapedString_DoubleEscapesBackslashes()
    {
        // Allow-list-free: caller-supplied pre-escapes get re-escaped.
        Assert.Equal("\\\\\\*", DiscordMarkdownEscaper.Escape("\\*"));
    }

    [Fact]
    public void Escape_NonReservedPrintableAscii_PassesThroughUnchanged()
    {
        // Negative parity (08 §6.2): Discord escapes only its 7-char set, so every
        // other printable ASCII char — including '.', '!', '[', '(' which Telegram
        // MarkdownV2 escapes — must pass through unchanged.
        const string reserved = "*_~`>|\\";
        var safe = new System.Text.StringBuilder();
        for (var c = ' '; c <= '~'; c++)
        {
            if (!reserved.Contains(c))
            {
                safe.Append(c);
            }
        }

        var input = safe.ToString();
        Assert.Equal(input, DiscordMarkdownEscaper.Escape(input));
    }
}
