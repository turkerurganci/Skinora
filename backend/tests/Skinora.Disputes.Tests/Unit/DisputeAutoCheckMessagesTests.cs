using Skinora.Disputes.Application.AutoCheckers;
using Xunit;

namespace Skinora.Disputes.Tests.Unit;

/// <summary>
/// WP17 — unit coverage for the dispute auto-check message localization
/// (<see cref="DisputeAutoCheckMessages.Localize"/>): per-locale rendering,
/// culture-tag normalization, and the English / key fallback chain.
/// </summary>
[Trait("Category", "Unit")]
public class DisputeAutoCheckMessagesTests
{
    [Theory]
    [InlineData("en", "No payment was found on the blockchain")]
    [InlineData("tr", "Blockchain üzerinde ödeme bulunamadı")]
    [InlineData("tr-TR", "Blockchain üzerinde ödeme bulunamadı")]
    [InlineData("es", "No se encontró ningún pago en la blockchain")]
    [InlineData("zh", "区块链上未找到付款")]
    public void Localize_RendersKnownKey_PerLocale(string locale, string expected)
    {
        Assert.Equal(
            expected,
            DisputeAutoCheckMessages.Localize(DisputeAutoCheckMessages.PaymentNotFound, locale));
    }

    [Fact]
    public void Localize_UnknownLocale_FallsBackToEnglish()
    {
        var en = DisputeAutoCheckMessages.Localize(DisputeAutoCheckMessages.WrongItemMatch, "en");
        Assert.Equal(en, DisputeAutoCheckMessages.Localize(DisputeAutoCheckMessages.WrongItemMatch, "fr"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Localize_NullOrEmptyLocale_FallsBackToEnglish(string? locale)
    {
        var en = DisputeAutoCheckMessages.Localize(DisputeAutoCheckMessages.DeliveryDelivered, "en");
        Assert.Equal(en, DisputeAutoCheckMessages.Localize(DisputeAutoCheckMessages.DeliveryDelivered, locale));
    }

    [Fact]
    public void Localize_UnknownKey_ReturnsKeyItself()
    {
        Assert.Equal("UNKNOWN_KEY", DisputeAutoCheckMessages.Localize("UNKNOWN_KEY", "en"));
    }
}
