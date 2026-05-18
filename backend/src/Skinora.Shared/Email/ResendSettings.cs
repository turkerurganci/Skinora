namespace Skinora.Shared.Email;

/// <summary>
/// Resend email provider configuration (T78 — 08 §4.1–§4.3).
/// Bound from the <c>Resend</c> section of <c>appsettings.json</c> or
/// the equivalent <c>Resend__*</c> environment variables.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Provider"/> switch picks between the production
/// Resend-backed transport and the development/test logging stub so a
/// misconfigured local environment cannot accidentally contact the
/// Resend API. CI and integration tests leave <see cref="Provider"/>
/// at <c>logging</c>; production environments must set it to <c>resend</c>
/// and supply <see cref="ApiKey"/>, <see cref="FromAddress"/> and
/// <see cref="WebhookSigningSecret"/>.
/// </para>
/// <para>
/// Secret values (<see cref="ApiKey"/>, <see cref="WebhookSigningSecret"/>)
/// must come from Docker Secrets / vault in production (05 §3.5); the
/// <c>REPLACE_IN_ENV</c> defaults in <c>appsettings.json</c> are a
/// deliberate trip-wire so an unconfigured deployment fails closed.
/// </para>
/// </remarks>
public sealed class ResendSettings
{
    public const string SectionName = "Resend";

    public const string ProviderResend = "resend";
    public const string ProviderLogging = "logging";

    /// <summary>
    /// Active provider — <c>resend</c> wires the real HTTP transport,
    /// <c>logging</c> keeps the T35/T37 stub implementations. Defaults
    /// to <c>logging</c> so a fresh checkout never sends real email.
    /// </summary>
    public string Provider { get; set; } = ProviderLogging;

    /// <summary>
    /// Resend API key — sent as <c>Authorization: Bearer {ApiKey}</c>
    /// on every <c>POST /emails</c> call (08 §4.2). Required when
    /// <see cref="Provider"/> is <c>resend</c>.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Resend API base URL. Override only when targeting a regional
    /// endpoint or a local recording proxy (e.g. WireMock during
    /// integration tests). Default is the documented public endpoint.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.resend.com";

    /// <summary>
    /// Default <c>From</c> address — must be on a verified Resend
    /// domain (DKIM/SPF/DMARC per 08 §4.2). Accepts the
    /// <c>"Display Name &lt;mailbox@domain&gt;"</c> RFC 5322 format.
    /// </summary>
    public string FromAddress { get; set; } = "Skinora <noreply@skinora.com>";

    /// <summary>
    /// Per-request HTTP timeout. Resend's send latency is sub-second in
    /// the happy path; a 10 second budget keeps the deferred-tier
    /// scheduling responsive without false-positive failing on a flaky
    /// network blip.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Webhook signing secret from the Resend dashboard. Stored with the
    /// Svix <c>whsec_</c> prefix; the verifier strips the prefix and
    /// base64-decodes the remainder to obtain the HMAC key. Required when
    /// <see cref="Provider"/> is <c>resend</c> — missing value forces 401
    /// on every inbound webhook.
    /// </summary>
    public string WebhookSigningSecret { get; set; } = string.Empty;

    /// <summary>
    /// Maximum allowed skew between the <c>svix-timestamp</c> header and
    /// backend UTC clock (08 §4.3 — 5 minutes). Mirrors the existing
    /// Steam / blockchain webhook replay window.
    /// </summary>
    public int WebhookReplayWindowSeconds { get; set; } = 300;
}
