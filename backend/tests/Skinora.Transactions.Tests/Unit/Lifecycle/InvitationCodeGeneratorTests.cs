using Skinora.Transactions.Application.Lifecycle;

namespace Skinora.Transactions.Tests.Unit.Lifecycle;

/// <summary>
/// Unit coverage for <see cref="InvitationCodeGenerator"/> — confirms the
/// emitted token is URL-safe (no padding, no '+' or '/'), short enough to
/// fit a clean URL, and unique enough to make brute-force lookup
/// infeasible (06 §3.5 InviteToken).
/// </summary>
public class InvitationCodeGeneratorTests
{
    [Fact]
    public void Generate_Returns_Url_Safe_Token()
    {
        var generator = new InvitationCodeGenerator();

        var token = generator.Generate();

        Assert.False(string.IsNullOrEmpty(token));
        Assert.DoesNotContain('=', token);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
    }

    [Fact]
    public void Generate_Has_22_Chars_For_16_Random_Bytes()
    {
        var generator = new InvitationCodeGenerator();

        var token = generator.Generate();

        // 16 bytes → 24 base64 chars → 22 after stripping '=' padding.
        Assert.Equal(22, token.Length);
    }

    [Fact]
    public void Generate_Returns_Distinct_Tokens()
    {
        var generator = new InvitationCodeGenerator();
        var tokens = new HashSet<string>();
        for (var i = 0; i < 1000; i++)
        {
            Assert.True(tokens.Add(generator.Generate()),
                "InvitationCodeGenerator emitted a duplicate within 1000 calls.");
        }
    }
}
