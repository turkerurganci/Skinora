using System.Text.Json;
using Microsoft.Extensions.Options;
using Skinora.Auth.Configuration;
using Skinora.Shared.Models;

namespace Skinora.API.RateLimiting;

/// <summary>
/// Reads <see cref="RateLimitAttribute"/> from the matched endpoint, computes
/// a partition key (user ID for authenticated policies, client IP otherwise)
/// and consults <see cref="IRateLimiterStore"/>. On every limited response it
/// stamps the X-RateLimit-* headers (07 §2.9). When the limit is exceeded it
/// short-circuits with a 429 + Retry-After + the platform error envelope
/// (07 §2.4) instead of forwarding the request.
///
/// Pipeline placement: AFTER UseAuthentication (we need the user ID claim)
/// and BEFORE UseAuthorization (don't burn permission checks on blocked
/// requests).
/// </summary>
public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimiterStore _store;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RateLimitMiddleware> _logger;

    /// <summary>Error code returned in the envelope when the limit is hit.</summary>
    public const string RateLimitErrorCode = "RATE_LIMIT_EXCEEDED";

    /// <summary>Policy names that key on the authenticated user ID.</summary>
    private static readonly HashSet<string> UserScopedPolicies = new(StringComparer.OrdinalIgnoreCase)
    {
        "user-read",
        "user-write",
        "steam-inventory",
        "admin-read",
        "admin-write",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public RateLimitMiddleware(
        RequestDelegate next,
        IRateLimiterStore store,
        IOptions<RateLimitOptions> options,
        ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        var attribute = context.GetEndpoint()?.Metadata.GetMetadata<RateLimitAttribute>();
        if (attribute is null)
        {
            // Opt-in: endpoints without [RateLimit] are not throttled.
            await _next(context);
            return;
        }

        if (!_options.Policies.TryGetValue(attribute.PolicyName, out var policy))
        {
            // Misconfiguration is loud — fail fast so it surfaces in dev/CI.
            throw new InvalidOperationException(
                $"Rate-limit policy '{attribute.PolicyName}' is not defined in configuration.");
        }

        var partitionKey = ResolvePartitionKey(context, attribute.PolicyName);
        var result = await _store.CheckAndIncrementAsync(
            attribute.PolicyName,
            policy,
            partitionKey,
            context.RequestAborted);

        WriteRateLimitHeaders(context, result);

        if (result.Allowed)
        {
            await _next(context);
            return;
        }

        await WriteRateLimitedResponseAsync(context, result);
    }

    /// <summary>
    /// User-scoped policies key on the JWT 'sub' claim — falls back to the
    /// client IP if the request is anonymous (defensive: lets the limiter still
    /// work if a user-scoped policy is misapplied to an anonymous endpoint).
    /// IP-scoped policies (auth, public) always key on the client IP.
    /// </summary>
    private static string ResolvePartitionKey(HttpContext context, string policyName)
    {
        if (UserScopedPolicies.Contains(policyName))
        {
            var userId = context.User.FindFirst(AuthClaimTypes.UserId)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                return $"user:{userId}";
            }
        }

        return $"ip:{GetClientIp(context)}";
    }

    private static string GetClientIp(HttpContext context)
    {
        // Connection.RemoteIpAddress is what UseForwardedHeaders rewrites
        // when the reverse proxy is configured (T11+). Until then this is the
        // direct socket peer.
        var ip = context.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrEmpty(ip) ? "unknown" : ip;
    }

    private static void WriteRateLimitHeaders(HttpContext context, RateLimitResult result)
    {
        context.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = result.ResetAtUnixSeconds.ToString();
    }

    private async Task WriteRateLimitedResponseAsync(HttpContext context, RateLimitResult result)
    {
        _logger.LogWarning(
            "Rate limit exceeded. Path: {Path} Limit: {Limit} RetryAfter: {RetryAfter}s TraceId: {TraceId}",
            context.Request.Path,
            result.Limit,
            result.RetryAfterSeconds,
            context.TraceIdentifier);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";
        context.Response.Headers["Retry-After"] = result.RetryAfterSeconds.ToString();

        var envelope = ApiResponse<object>.Fail(
            code: RateLimitErrorCode,
            message: "Rate limit exceeded. Please retry after the indicated period.",
            details: null,
            traceId: context.TraceIdentifier);

        await context.Response.WriteAsync(JsonSerializer.Serialize(envelope, JsonOptions));
    }
}
