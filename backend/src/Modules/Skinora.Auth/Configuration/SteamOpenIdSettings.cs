namespace Skinora.Auth.Configuration;

/// <summary>
/// Steam OpenID 2.0 and Steam Web API configuration — 08 §2.1, §2.2.
/// </summary>
public sealed class SteamOpenIdSettings
{
    public const string SectionName = "SteamOpenId";

    /// <summary>
    /// <c>openid.realm</c> — root domain of the platform, e.g. <c>https://skinora.com</c>.
    /// Steam uses this to display the requesting site during login consent.
    /// </summary>
    public required string Realm { get; init; }

    /// <summary>
    /// <c>openid.return_to</c> — fully-qualified callback URL Steam redirects
    /// to after consent, e.g. <c>https://skinora.com/api/v1/auth/steam/callback</c>.
    /// </summary>
    public required string ReturnToUrl { get; init; }

    /// <summary>
    /// <c>openid.return_to</c> for the re-verify flow — A5/A6 (07 §4.6–§4.7).
    /// Distinct from <see cref="ReturnToUrl"/> so a login-flow assertion cannot
    /// be replayed against the re-verify callback.
    /// </summary>
    public required string ReVerifyReturnToUrl { get; init; }

    /// <summary>
    /// Frontend URL prefix for the post-login redirect landing page
    /// (e.g. <c>https://skinora.com/auth/callback</c>). Server appends
    /// <c>?status=...</c> / <c>?error=...</c> per 07 §4.3.
    /// </summary>
    public required string FrontendCallbackUrl { get; init; }

    /// <summary>
    /// Default frontend path used when <c>returnUrl</c> is missing or rejected
    /// by <see cref="Application.Security.IReturnUrlValidator"/> — 07 §4.2.
    /// Stored as an absolute path (e.g. <c>/dashboard</c>).
    /// </summary>
    public string DefaultReturnPath { get; init; } = "/dashboard";

    /// <summary>
    /// Steam Web API key for <c>GetPlayerSummaries</c> — 08 §2.2. Optional:
    /// when empty, login still succeeds but profile display name / avatar
    /// fall back to a placeholder. Never logged in plaintext.
    /// </summary>
    public string? WebApiKey { get; init; }
}
