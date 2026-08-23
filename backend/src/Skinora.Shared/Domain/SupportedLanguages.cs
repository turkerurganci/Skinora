namespace Skinora.Shared.Domain;

/// <summary>
/// Platformun desteklediği arayüz/bildirim dilleri — 07 §5.10 (MVP dilleri).
///
/// <para>
/// <b>Neden burada:</b> bu liste iki modülde birden gerekiyor —
/// <c>Skinora.Users.LanguageService</c> (U8, kullanıcı dil tercihini
/// günceller) ve <c>Skinora.Auth.UserProvisioningService</c> (F4, kayıt anında
/// arayüz dilini saklar). İkinci çağrı yeri eklenirken listeyi kopyalamak,
/// ileride birinin diğerinden sapabileceği <b>iki doğruluk kaynağı</b>
/// yaratırdı; bu projenin kayıtlı kusur ailelerinden biri tam olarak budur.
/// </para>
///
/// <para>
/// <b>Frontend ile bağ:</b> <c>frontend/src/i18n/routing.ts</c> aynı dört
/// yerel ayarı taşır. Buraya bir dil eklerken orası ve dört mesaj dosyası da
/// güncellenmelidir — i18n parity kontrolü (<c>npm run i18n:check</c>) eksik
/// anahtarı yakalar, ama <b>eksik yerel ayarı</b> yakalamaz.
/// </para>
/// </summary>
public static class SupportedLanguages
{
    /// <summary>Bir dil çözülemediğinde kullanılan taban dil.</summary>
    public const string Default = "en";

    private static readonly HashSet<string> Allowed =
        new(StringComparer.OrdinalIgnoreCase) { "en", "zh", "es", "tr" };

    /// <summary>Desteklenen dil kodları (küçük harf).</summary>
    public static IReadOnlyCollection<string> All => Allowed;

    /// <summary><paramref name="language"/> desteklenen bir dil mi.</summary>
    public static bool IsSupported(string? language) =>
        !string.IsNullOrWhiteSpace(language) && Allowed.Contains(language);

    /// <summary>
    /// Desteklenen dili küçük harfe normalize eder; desteklenmiyorsa
    /// <see cref="Default"/> döner. Kullanıcı girdisinden (URL yolu, tarayıcı
    /// başlığı) dil türetirken kullanılır — geçersiz değer <b>hata değil</b>,
    /// taban dile düşüş sebebidir.
    /// </summary>
    public static string NormalizeOrDefault(string? language) =>
        IsSupported(language) ? language!.ToLowerInvariant() : Default;

    /// <summary>
    /// Uygulama-içi bir yolun ilk segmentinden yerel ayarı çıkarır
    /// (örn. "/tr/dashboard" → "tr"). Desteklenmeyen ya da yolsuz bir değer
    /// <c>null</c> döner — bu bir hata değil, kullanıcı girdisinden türetilen
    /// bir ipucunun doğrulanmasıdır ve çağıran taban dile düşer.
    /// </summary>
    public static string? FromPathPrefix(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var trimmed = path.TrimStart('/');
        var slash = trimmed.IndexOf('/');
        var first = slash >= 0 ? trimmed[..slash] : trimmed;

        // Tek segmentli yollarda sorgu/parça eki olabilir: "tr?x=1".
        var cut = first.IndexOfAny(['?', '#']);
        if (cut >= 0) first = first[..cut];

        return IsSupported(first) ? first.ToLowerInvariant() : null;
    }
}
