namespace Skinora.API.RateLimiting;

/// <summary>
/// Marks a controller or action with the rate-limit policy that applies to
/// it. The middleware reads this attribute from the matched endpoint's
/// metadata and looks the policy up in <see cref="RateLimitOptions.Policies"/>.
///
/// Endpoints without the attribute are not rate-limited (opt-in model — see
/// T07 design notes).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RateLimitAttribute : Attribute
{
    public RateLimitAttribute(string policyName)
    {
        PolicyName = policyName;
    }

    /// <summary>Policy name as defined in appsettings (e.g. "auth", "user-read").</summary>
    public string PolicyName { get; }

    /// <summary>
    /// WP11 — when true, a rate-limit rejection on this endpoint is surfaced as
    /// a 302 redirect to the frontend Steam callback with
    /// <c>?error=temporarily_locked&amp;retryAfter=N</c> instead of a 429 JSON
    /// envelope (05 §6.3 abuse throttling, 07 §4.2 A1). Set this only on
    /// browser-navigation auth endpoints (e.g. <c>GET /auth/steam</c>) where the
    /// caller is a full-page navigation, not a fetch that can read JSON.
    /// </summary>
    public bool RedirectToSteamCallbackOnReject { get; init; }
}
