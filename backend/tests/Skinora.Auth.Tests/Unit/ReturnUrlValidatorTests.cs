using Skinora.Auth.Application.SteamAuthentication;

namespace Skinora.Auth.Tests.Unit;

public class ReturnUrlValidatorTests
{
    private readonly ReturnUrlValidator _validator = new("/dashboard");

    [Theory]
    [InlineData("/transactions/abc")]
    [InlineData("/profile")]
    [InlineData("/")]
    public void Sanitize_RelativePath_PassesThrough(string input)
    {
        Assert.Equal(input, _validator.Sanitize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://evil.com/dashboard")]
    [InlineData("http://evil.com")]
    [InlineData("//evil.com/dashboard")]
    [InlineData("\\evil.com")]
    [InlineData("dashboard")]            // not rooted
    [InlineData("javascript:alert(1)")]  // protocol
    public void Sanitize_RejectsDangerousOrMalformed_ReturnsDefault(string? input)
    {
        Assert.Equal("/dashboard", _validator.Sanitize(input));
    }
}
