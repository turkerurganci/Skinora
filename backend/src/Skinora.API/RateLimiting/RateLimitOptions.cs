namespace Skinora.API.RateLimiting;

/// <summary>
/// Configuration root bound from the "RateLimit" section of appsettings.
/// Defines all rate-limit policies referenced by [RateLimit("name")] attributes.
/// Spec: 07 §2.9, 05 §6.3.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>Master switch — when false, the middleware is short-circuited.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Redis key prefix for all rate-limit counters. Lets us share a Redis
    /// instance with other features without colliding.
    /// </summary>
    public string KeyPrefix { get; set; } = "skinora:ratelimit";

    /// <summary>Map of policy name → limit definition.</summary>
    public Dictionary<string, RateLimitPolicy> Policies { get; set; } = new();
}

/// <summary>
/// Single rate-limit policy: how many requests are allowed per window.
/// </summary>
public sealed class RateLimitPolicy
{
    /// <summary>Maximum number of requests permitted within the window.</summary>
    public int Limit { get; set; }

    /// <summary>Window length in seconds (fixed window algorithm).</summary>
    public int WindowSeconds { get; set; }
}
