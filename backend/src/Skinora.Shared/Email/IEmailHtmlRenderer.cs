namespace Skinora.Shared.Email;

/// <summary>
/// Wraps the localized notification title + body into the HTML payload
/// sent to Resend (T78 — 08 §4.2 "Email şablonları"). Category-aware
/// so transaction / security / account / timeout emails carry distinct
/// accent colour + footer copy without duplicating .resx entries. Lives
/// in Skinora.Shared so both notification dispatch (Notifications
/// module) and the verification-email sender (Users module) can render
/// matching chrome without pulling a cross-module reference.
/// </summary>
public interface IEmailHtmlRenderer
{
    EmailHtmlRendererResult Render(
        EmailCategory category,
        string locale,
        string title,
        string body);
}

/// <summary>HTML wrapper output — body suitable for the Resend <c>html</c> field plus a plain-text fallback for the <c>text</c> field.</summary>
public sealed record EmailHtmlRendererResult(string Html, string Text);
