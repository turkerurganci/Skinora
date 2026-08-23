using Skinora.Shared.Domain;

namespace Skinora.Shared.Tests.Unit;

/// <summary>
/// F4 — <c>UITour-SignupLanguageHardcodedEn</c>.
///
/// <para>
/// Bu tip iki modülün paylaştığı TEK doğruluk kaynağı: <c>LanguageService</c>
/// (U8 — kullanıcı tercihini günceller) ve <c>UserProvisioningService</c>
/// (kayıt anında arayüz dilini saklar). Liste kopyalansaydı ikisi ileride
/// sapabilirdi; bu testler listenin ve türetme kurallarının davranışını sabitler.
/// </para>
///
/// <para>
/// <c>FromPathPrefix</c> özellikle önemli çünkü girdisi <b>kullanıcı
/// kontrolündeki</b> bir return URL'i: desteklenmeyen bir değerin sessizce
/// kabul edilmesi, kullanıcının bildirim dilini geçersiz bir koda çevirir ve
/// i18n kaynak araması taban dile düşerken kayıt bozuk kalır.
/// </para>
/// </summary>
public sealed class SupportedLanguagesTests
{
    [Fact]
    public void All_ContainsExactlyTheMvpLocales()
    {
        // Frontend routing.ts aynı dörtlüyü taşıyor; buraya bir dil eklenirken
        // orası ve dört mesaj dosyası da güncellenmeli (07 §5.10).
        Assert.Equal(
            new[] { "en", "es", "tr", "zh" },
            SupportedLanguages.All.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    [InlineData("es")]
    [InlineData("zh")]
    [InlineData("TR")] // büyük/küçük harf duyarsız
    public void IsSupported_TrueForMvpLocales(string language)
        => Assert.True(SupportedLanguages.IsSupported(language));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("de")]
    [InlineData("tr-TR")] // bölge ekli kod desteklenmiyor
    public void IsSupported_FalseForEverythingElse(string? language)
        => Assert.False(SupportedLanguages.IsSupported(language));

    [Theory]
    [InlineData("TR", "tr")]
    [InlineData("en", "en")]
    public void NormalizeOrDefault_LowercasesSupported(string input, string expected)
        => Assert.Equal(expected, SupportedLanguages.NormalizeOrDefault(input));

    [Theory]
    [InlineData(null)]
    [InlineData("de")]
    [InlineData("")]
    public void NormalizeOrDefault_FallsBackToDefault(string? input)
        => Assert.Equal(SupportedLanguages.Default, SupportedLanguages.NormalizeOrDefault(input));

    [Theory]
    [InlineData("/tr/dashboard", "tr")]
    [InlineData("tr/dashboard", "tr")]
    [InlineData("/es/transactions/new", "es")]
    [InlineData("/zh", "zh")]
    [InlineData("/tr?next=1", "tr")]
    [InlineData("/tr#top", "tr")]
    [InlineData("/TR/dashboard", "tr")]
    public void FromPathPrefix_ExtractsLocale(string path, string expected)
        => Assert.Equal(expected, SupportedLanguages.FromPathPrefix(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/dashboard")]        // yerel ayar öneki yok
    [InlineData("/de/dashboard")]     // desteklenmeyen dil
    [InlineData("/tr-TR/dashboard")]  // bölge ekli kod
    public void FromPathPrefix_ReturnsNullWhenNoSupportedLocale(string? path)
    {
        // null dönmesi ÖNEMLİ: çağıran taban dile düşer. Burada "en" döndürmek,
        // "kullanıcı İngilizce seçti" ile "dil bilinmiyor"u aynı şey yapardı.
        Assert.Null(SupportedLanguages.FromPathPrefix(path));
    }
}
