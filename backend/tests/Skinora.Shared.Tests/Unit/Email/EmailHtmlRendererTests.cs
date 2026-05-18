using Skinora.Shared.Email;

namespace Skinora.Shared.Tests.Unit.Email;

public sealed class EmailHtmlRendererTests
{
    private readonly EmailHtmlRenderer _renderer = new();

    [Theory]
    [InlineData(EmailCategory.Transaction, "en", "Transaction update")]
    [InlineData(EmailCategory.Transaction, "tr", "İşlem güncellemesi")]
    [InlineData(EmailCategory.Security, "tr", "Güvenlik bildirimi")]
    [InlineData(EmailCategory.Account, "es", "Cuenta")]
    [InlineData(EmailCategory.Timeout, "zh", "时效提醒")]
    public void Render_BannerText_MatchesCategoryAndLocale(EmailCategory category, string locale, string expectedBanner)
    {
        var result = _renderer.Render(category, locale, "Title", "Body");
        Assert.Contains(expectedBanner, result.Html);
        Assert.Contains(expectedBanner, result.Text);
    }

    [Fact]
    public void Render_HtmlEscapesUserContent()
    {
        var result = _renderer.Render(
            EmailCategory.Transaction,
            "en",
            "<script>alert('x')</script>",
            "Normal & body");

        Assert.DoesNotContain("<script>", result.Html);
        Assert.Contains("&lt;script&gt;", result.Html);
        Assert.Contains("Normal &amp; body", result.Html);
    }

    [Fact]
    public void Render_NewlineInBody_ConvertsToBr()
    {
        var result = _renderer.Render(
            EmailCategory.Account,
            "en",
            "Title",
            "Line 1\nLine 2");

        Assert.Contains("Line 1<br />Line 2", result.Html);
    }

    [Fact]
    public void Render_UnknownLocale_FallsBackToEnglish()
    {
        var result = _renderer.Render(EmailCategory.Transaction, "xx", "Title", "Body");
        Assert.Contains("Transaction update", result.Html);
    }

    [Fact]
    public void Render_FullCultureCode_NormalizesToTwoLetter()
    {
        var result = _renderer.Render(EmailCategory.Security, "tr-TR", "Title", "Body");
        Assert.Contains("Güvenlik bildirimi", result.Html);
    }

    [Fact]
    public void Render_TextOutput_ContainsTitleAndBody()
    {
        var result = _renderer.Render(EmailCategory.Account, "en", "Hello", "World");

        Assert.Contains("Hello", result.Text);
        Assert.Contains("World", result.Text);
        Assert.Contains("Skinora", result.Text);
    }
}
