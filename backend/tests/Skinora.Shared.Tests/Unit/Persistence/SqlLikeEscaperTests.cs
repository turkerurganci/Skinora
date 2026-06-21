using Skinora.Shared.Persistence;
using Xunit;

namespace Skinora.Shared.Tests.Unit.Persistence;

public class SqlLikeEscaperTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("plain text with no wildcards", "plain text with no wildcards")]
    [InlineData("[", "[[]")]
    [InlineData("%", "[%]")]
    [InlineData("_", "[_]")]
    [InlineData("100%", "100[%]")]
    [InlineData("steam_id", "steam[_]id")]
    [InlineData("a%b_c[d", "a[%]b[_]c[[]d")]
    public void Escape_WildcardChars_AreBracketWrapped(string input, string expected)
    {
        Assert.Equal(expected, SqlLikeEscaper.Escape(input));
    }

    [Fact]
    public void Escape_RewritesOpenBracketFirst_SoIntroducedBracketsAreNotReprocessed()
    {
        // If '_' or '%' were escaped before '[', the '[' introduced by their
        // bracket-wrapping ("[_]" / "[%]") would itself be re-escaped, corrupting
        // the pattern. A lone '_' must therefore yield exactly "[_]", not "[[]_]".
        Assert.Equal("[_]", SqlLikeEscaper.Escape("_"));
        Assert.Equal("[%]", SqlLikeEscaper.Escape("%"));
    }
}
