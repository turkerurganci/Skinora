namespace Skinora.API.Configuration;

/// <summary>
/// Reverse-proxy trust for <c>UseForwardedHeaders</c> — F3a, kapatılan bulgu
/// <c>ForwardedHeadersNotRegistered</c> (🔴).
///
/// <para>
/// <b>Sorun:</b> nginx <c>X-Real-IP</c> + <c>X-Forwarded-For</c> gönderiyordu ama
/// backend bunları hiç okumuyordu, dolayısıyla
/// <c>HttpContext.Connection.RemoteIpAddress</c> her istekte <b>proxy'nin
/// IP'siydi</b>. Canlı kanıt: gerçek bir Steam girişinin
/// <c>UserLoginLogs.IpAddress</c> değeri <c>::ffff:172.20.0.5</c> — nginx
/// container'ının kendisi. Sonucu üç güvenlik kontrolü taşıyordu (auth rate
/// limit izolasyonu · geo-block · VPN sinyali) ve üçü de hata vermeden etkisiz
/// kalıyordu.
/// </para>
///
/// <para>
/// <b>Düzeltmenin tuzağı ve buradaki duruş:</b> <c>X-Forwarded-For</c>'a körü
/// körüne güvenmek daha kötüsüdür — istemci kendi IP'sini uydurup rate limit'i
/// ve geo-block'u <i>birlikte</i> atlar. Bu yüzden güven <b>açıkça
/// yapılandırılan</b> proxy'lerle sınırlıdır ve
/// <see cref="IsConfigured"/> yanlışsa <c>UseForwardedHeaders</c>
/// <b>hiç kaydedilmez</b>: yapılandırılmamış bir ortam bugünkü davranışında
/// kalır (istemci IP'si görünmez) — sessizce spoofing'e açılmaz. Yanlış
/// yapılandırmanın bedeli "eksik bilgi" olmalı, "yanlış güven" değil.
/// </para>
/// </summary>
public sealed class ReverseProxySettings
{
    public const string SectionName = "ReverseProxy";

    /// <summary>
    /// Güvenilen tekil proxy IP'leri (örn. <c>172.20.0.5</c>). Container'lar
    /// yeniden yaratıldığında IP değişebildiği için tek başına kırılgandır;
    /// <see cref="KnownNetworks"/> tercih edilir.
    /// </summary>
    public string[] KnownProxies { get; init; } = [];

    /// <summary>
    /// Güvenilen ağlar, CIDR biçiminde (örn. <c>172.20.0.0/16</c> — docker
    /// compose ağı). Üretimde <b>yalnız</b> reverse proxy'nin bulunduğu ağ
    /// yazılmalıdır; geniş bir aralık vermek güveni sulandırır.
    /// </summary>
    public string[] KnownNetworks { get; init; } = [];

    /// <summary>
    /// Zincirde kaç proxy olduğu. Varsayılan 1 — tek nginx (<c>§G.3</c>
    /// tek-origin topolojisi). Önünde CDN varsa artırılmalıdır; olduğundan
    /// büyük vermek istemcinin zincire sahte girdi eklemesine izin verir.
    /// </summary>
    public int ForwardLimit { get; init; } = 1;

    /// <summary>
    /// En az bir güvenilen proxy/ağ tanımlıysa true. Yanlışsa forwarded
    /// header'lar <b>hiç işlenmez</b> (yukarıdaki duruş).
    /// </summary>
    public bool IsConfigured => KnownProxies.Length > 0 || KnownNetworks.Length > 0;
}
