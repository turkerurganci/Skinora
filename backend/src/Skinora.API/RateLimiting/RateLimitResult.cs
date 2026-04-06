namespace Skinora.API.RateLimiting;

/// <summary>
/// Result of a single rate-limit check. Carries everything the middleware
/// needs to populate response headers and (when blocked) build the 429 body.
/// </summary>
/// <param name="Allowed">True when the request is within the limit.</param>
/// <param name="Limit">Configured request limit for the policy.</param>
/// <param name="Remaining">Requests remaining in the current window (never negative).</param>
/// <param name="ResetAtUnixSeconds">Unix epoch (seconds) when the current window expires.</param>
/// <param name="RetryAfterSeconds">Suggested wait before the next allowed attempt — only meaningful when Allowed = false.</param>
public readonly record struct RateLimitResult(
    bool Allowed,
    int Limit,
    int Remaining,
    long ResetAtUnixSeconds,
    int RetryAfterSeconds);
