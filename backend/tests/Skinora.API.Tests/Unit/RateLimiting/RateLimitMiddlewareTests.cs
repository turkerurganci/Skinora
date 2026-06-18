using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Skinora.API.RateLimiting;
using Skinora.Auth.Configuration;

namespace Skinora.API.Tests.Unit.RateLimiting;

/// <summary>
/// WP11 — focused tests for the brute-force redirect branch added to
/// <see cref="RateLimitMiddleware"/> (05 §6.3, 07 §4.2 A1). Exercised directly
/// against a <see cref="DefaultHttpContext"/> so the Steam OpenID controller
/// (not serviceable in the generic integration factory) is not required.
/// </summary>
public class RateLimitMiddlewareTests
{
    private const string FrontendCallback = "https://app.skinora.test/auth/callback";

    private sealed class FixedResultStore : IRateLimiterStore
    {
        private readonly bool _allowed;
        private readonly int _retryAfter;

        public FixedResultStore(bool allowed, int retryAfter = 0)
        {
            _allowed = allowed;
            _retryAfter = retryAfter;
        }

        public Task<RateLimitResult> CheckAndIncrementAsync(
            string policyName, RateLimitPolicy policy, string partitionKey, CancellationToken ct)
            => Task.FromResult(new RateLimitResult(
                Allowed: _allowed,
                Limit: policy.Limit,
                Remaining: _allowed ? policy.Limit - 1 : 0,
                ResetAtUnixSeconds: 0,
                RetryAfterSeconds: _retryAfter));
    }

    private static RateLimitMiddleware CreateSut(RequestDelegate next, IRateLimiterStore store)
    {
        var options = Options.Create(new RateLimitOptions
        {
            Enabled = true,
            Policies = new() { ["auth"] = new RateLimitPolicy { Limit = 10, WindowSeconds = 60 } },
        });
        var steam = Options.Create(new SteamOpenIdSettings
        {
            Realm = "https://app.skinora.test",
            ReturnToUrl = "https://app.skinora.test/api/v1/auth/steam/callback",
            ReVerifyReturnToUrl = "https://app.skinora.test/api/v1/auth/steam/re-verify/callback",
            FrontendCallbackUrl = FrontendCallback,
        });
        return new RateLimitMiddleware(
            next, store, options, steam, NullLogger<RateLimitMiddleware>.Instance);
    }

    private static HttpContext BuildContext(bool redirectFlag)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var attr = new RateLimitAttribute("auth") { RedirectToSteamCallbackOnReject = redirectFlag };
        ctx.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask, new EndpointMetadataCollection(attr), "test-endpoint"));
        return ctx;
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Rejected_WithRedirectFlag_Returns302ToCallbackWithTemporarilyLocked()
    {
        var nextCalled = false;
        var sut = CreateSut(_ => { nextCalled = true; return Task.CompletedTask; },
            new FixedResultStore(allowed: false, retryAfter: 300));
        var ctx = BuildContext(redirectFlag: true);

        await sut.InvokeAsync(ctx);

        Assert.False(nextCalled); // short-circuited
        Assert.Equal(StatusCodes.Status302Found, ctx.Response.StatusCode);
        var location = ctx.Response.Headers.Location.ToString();
        Assert.StartsWith(FrontendCallback, location);
        Assert.Contains("error=temporarily_locked", location);
        Assert.Contains("retryAfter=300", location);
        Assert.Equal("300", ctx.Response.Headers["Retry-After"].ToString());
        Assert.DoesNotContain("steamcommunity.com", location);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Rejected_WithoutRedirectFlag_Returns429JsonEnvelope()
    {
        var sut = CreateSut(_ => Task.CompletedTask,
            new FixedResultStore(allowed: false, retryAfter: 42));
        var ctx = BuildContext(redirectFlag: false);

        await sut.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status429TooManyRequests, ctx.Response.StatusCode);
        Assert.Equal("42", ctx.Response.Headers["Retry-After"].ToString());

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("RATE_LIMIT_EXCEEDED",
            doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(ctx.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Allowed_CallsNext_WithoutRedirectOr429()
    {
        var nextCalled = false;
        var sut = CreateSut(_ => { nextCalled = true; return Task.CompletedTask; },
            new FixedResultStore(allowed: true));
        var ctx = BuildContext(redirectFlag: true);

        await sut.InvokeAsync(ctx);

        Assert.True(nextCalled);
        Assert.NotEqual(StatusCodes.Status302Found, ctx.Response.StatusCode);
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, ctx.Response.StatusCode);
        // Headers are still stamped on the allowed path.
        Assert.True(ctx.Response.Headers.ContainsKey("X-RateLimit-Limit"));
    }
}
