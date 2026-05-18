namespace Skinora.Shared.Email;

/// <summary>
/// Coarse Resend email classification (T78 — 08 §4.2 "Email türleri").
/// Used by <see cref="IEmailHtmlRenderer"/> to pick the appropriate
/// HTML wrapper / accent colour / Resend tag so transactional, security
/// and account messages stay distinguishable in the user's inbox even
/// before the post-MVP templating pass (MVP-OUT-016).
/// </summary>
public enum EmailCategory
{
    /// <summary>Day-to-day trade lifecycle updates (ödeme alındı, item emanete alındı, …).</summary>
    Transaction,

    /// <summary>Security-relevant changes (wallet address changed, suspicious login, …).</summary>
    Security,

    /// <summary>Account lifecycle (welcome, deletion confirm, email verification, …).</summary>
    Account,

    /// <summary>Timeout / expiry warnings (payment window expiring, …).</summary>
    Timeout,
}
