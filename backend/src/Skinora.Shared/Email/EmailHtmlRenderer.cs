using System.Net;
using System.Text;

namespace Skinora.Shared.Email;

/// <summary>
/// Minimal HTML wrapper for Resend-bound emails (T78 — 08 §4.2). The
/// body content arrives already-localized through the .resx pipeline
/// (T37 — <c>NotificationTemplates.&lt;culture&gt;.resx</c>); this
/// renderer only adds chrome — a coloured banner (transaction / security
/// / account / timeout), the brand header, and a footer with the
/// opt-out hint. Polished, brand-finalised templates are post-MVP per
/// <c>MVP-OUT-016</c>.
/// </summary>
/// <remarks>
/// <para>
/// All user-supplied text (title + body) is HTML-escaped before being
/// placed inside the wrapper. Single line breaks in the body are
/// converted to <c>&lt;br /&gt;</c> so plain-text resource entries
/// render readably without forcing every callsite to author HTML.
/// </para>
/// </remarks>
public sealed class EmailHtmlRenderer : IEmailHtmlRenderer
{
    public EmailHtmlRendererResult Render(
        EmailCategory category,
        string locale,
        string title,
        string body)
    {
        var safeTitle = WebUtility.HtmlEncode(title ?? string.Empty);
        var safeBody = WebUtility.HtmlEncode(body ?? string.Empty).Replace("\n", "<br />");
        var banner = ResolveBanner(category, locale);
        var footer = ResolveFooter(locale);
        var accent = ResolveAccent(category);

        var html = new StringBuilder(capacity: 1024);
        html.Append("<!DOCTYPE html><html lang=\"").Append(WebUtility.HtmlEncode(locale)).Append("\"><head>");
        html.Append("<meta charset=\"utf-8\" /></head><body style=\"margin:0;padding:0;background-color:#f5f5f5;font-family:Segoe UI,Arial,sans-serif;color:#222;\">");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background-color:#f5f5f5;padding:24px 0;\"><tr><td align=\"center\">");
        html.Append("<table role=\"presentation\" width=\"560\" cellpadding=\"0\" cellspacing=\"0\" style=\"background-color:#ffffff;border-radius:8px;overflow:hidden;\">");

        // Category banner — trusted compile-time string, no encoding so
        // the localized characters stay readable in the rendered HTML.
        html.Append("<tr><td style=\"background-color:").Append(accent).Append(";padding:12px 24px;color:#ffffff;font-size:13px;font-weight:600;letter-spacing:0.5px;text-transform:uppercase;\">");
        html.Append(banner);
        html.Append("</td></tr>");

        // Brand header
        html.Append("<tr><td style=\"padding:24px 24px 0 24px;font-size:20px;font-weight:700;color:#111;\">Skinora</td></tr>");

        // Title
        html.Append("<tr><td style=\"padding:8px 24px 0 24px;font-size:18px;font-weight:600;color:#111;\">");
        html.Append(safeTitle);
        html.Append("</td></tr>");

        // Body
        html.Append("<tr><td style=\"padding:16px 24px 24px 24px;font-size:14px;line-height:1.55;color:#333;\">");
        html.Append(safeBody);
        html.Append("</td></tr>");

        // Footer — trusted compile-time string, no encoding (same reason as
        // banner). Title / body remain HtmlEncoded above because they
        // carry user / DB-derived content.
        html.Append("<tr><td style=\"padding:16px 24px;background-color:#fafafa;border-top:1px solid #eee;font-size:12px;color:#777;\">");
        html.Append(footer);
        html.Append("</td></tr>");

        html.Append("</table></td></tr></table></body></html>");

        var text = $"{banner}\n\nSkinora\n{title}\n\n{body}\n\n--\n{footer}";

        return new EmailHtmlRendererResult(html.ToString(), text);
    }

    private static string ResolveAccent(EmailCategory category) => category switch
    {
        EmailCategory.Transaction => "#0d6efd",
        EmailCategory.Security => "#dc3545",
        EmailCategory.Account => "#212529",
        EmailCategory.Timeout => "#fd7e14",
        _ => "#212529",
    };

    private static string ResolveBanner(EmailCategory category, string locale) => category switch
    {
        EmailCategory.Transaction => Pick(locale,
            en: "Transaction update",
            tr: "İşlem güncellemesi",
            es: "Actualización de la transacción",
            zh: "交易更新"),
        EmailCategory.Security => Pick(locale,
            en: "Security notice",
            tr: "Güvenlik bildirimi",
            es: "Aviso de seguridad",
            zh: "安全通知"),
        EmailCategory.Account => Pick(locale,
            en: "Account",
            tr: "Hesap",
            es: "Cuenta",
            zh: "账户"),
        EmailCategory.Timeout => Pick(locale,
            en: "Time-sensitive",
            tr: "Süre kritik",
            es: "Tiempo crítico",
            zh: "时效提醒"),
        _ => "Skinora",
    };

    private static string ResolveFooter(string locale) => Pick(locale,
        en: "You receive this transactional email because you have an active Skinora account. Manage email preferences in your account settings.",
        tr: "Bu işlem e-postasını aktif bir Skinora hesabınız olduğu için aldınız. E-posta tercihlerinizi hesap ayarlarınızdan yönetebilirsiniz.",
        es: "Recibe este correo transaccional porque tiene una cuenta activa de Skinora. Gestione sus preferencias de correo en la configuración de la cuenta.",
        zh: "您因拥有有效的 Skinora 账户而收到这封事务性邮件。请在账户设置中管理您的邮件偏好。");

    private static string Pick(string locale, string en, string tr, string es, string zh)
    {
        if (string.IsNullOrWhiteSpace(locale)) return en;

        // Two-letter language code (e.g. tr-TR → tr). Anything we don't
        // recognise falls back to English per 05 §7.3.
        var twoLetter = locale.Length >= 2 ? locale[..2].ToLowerInvariant() : locale.ToLowerInvariant();
        return twoLetter switch
        {
            "tr" => tr,
            "es" => es,
            "zh" => zh,
            _ => en,
        };
    }
}
