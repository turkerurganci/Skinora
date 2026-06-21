using Skinora.Shared.Discord;
using Skinora.Shared.Notifications;
using Skinora.Shared.Telegram;
using Xunit;

namespace Skinora.Shared.Tests.Unit.Notifications;

public class BoldHeaderMessageComposerTests
{
    private static string Telegram(string title, string body, int max = 4096) =>
        BoldHeaderMessageComposer.Compose(title, body, max, MarkdownV2Escaper.Escape, "*", "*");

    private static string Discord(string title, string body, int max = 2000) =>
        BoldHeaderMessageComposer.Compose(title, body, max, DiscordMarkdownEscaper.Escape, "**", "**");

    [Fact]
    public void Compose_ShortMessage_EscapesAndWrapsUnchanged()
    {
        // Fast path: identical to the pre-guard behaviour, so existing happy-path
        // delivery is unchanged.
        Assert.Equal("*Title*\n\nBody", Telegram("Title", "Body"));
        Assert.Equal("**Title**\n\nBody", Discord("Title", "Body"));
    }

    [Fact]
    public void Compose_EscapesReservedChars_NoLiveMarkdownSurvives()
    {
        // An item name carrying reserved chars (as a {ItemName} substitution would)
        // is escaped, never rendered as live markdown.
        var telegram = Telegram("Trade", "AK-47 | Redline *_~");
        Assert.Contains("AK\\-47 \\| Redline \\*\\_\\~", telegram);

        // Discord does not treat '-' as reserved, so only the markdown chars escape.
        var discord = Discord("Trade", "AK-47 | Redline *_~");
        Assert.Contains("AK-47 \\| Redline \\*\\_\\~", discord);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(4096)]
    public void Compose_OverLongBody_FitsWithinLimit_NoSplitEscapePair(int max)
    {
        // Every char reserved → escaping doubles length; the guard must truncate.
        var body = new string('*', max * 2);
        var result = max == 2000 ? Discord("Title", body, max) : Telegram("Title", body, max);

        Assert.True(result.Length <= max, $"length {result.Length} exceeds limit {max}");
        Assert.True(
            TrailingBackslashRunIsEven(result),
            "result ends in a dangling odd backslash run — a split escape pair");
        Assert.EndsWith("…", result); // truncation marker present
    }

    [Fact]
    public void Compose_OverLongTitle_TruncatesTitle_DropsBody_KeepsBoldMarkers()
    {
        var result = Telegram(new string('a', 5000), "this body is dropped", 4096);

        Assert.True(result.Length <= 4096, $"length {result.Length}");
        Assert.StartsWith("*", result); // opening bold marker intact
        Assert.Contains("…*", result); // ellipsis then the closing bold marker
        Assert.EndsWith("\n\n", result); // body dropped → message ends at the separator
        Assert.DoesNotContain("this body is dropped", result);
    }

    private static bool TrailingBackslashRunIsEven(string value)
    {
        var count = 0;
        for (var i = value.Length - 1; i >= 0 && value[i] == '\\'; i--)
        {
            count++;
        }

        return count % 2 == 0;
    }
}
