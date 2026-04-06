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
}
