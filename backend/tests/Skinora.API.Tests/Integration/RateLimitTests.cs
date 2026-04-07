using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skinora.API.RateLimiting;
using Skinora.API.Tests.Common;
using Skinora.Shared.Persistence;
using StackExchange.Redis;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// Spec: 07 §2.9 (rate limiting), 11 §T07.
///
/// These tests run with an in-memory <see cref="IRateLimiterStore"/> swapped
/// in via <c>ConfigureServices</c> so the suite never touches a real Redis.
/// Each fact builds its own factory (no <c>IClassFixture</c>) so counter
/// state never leaks between tests.
/// </summary>
public class RateLimitTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static HttpClient CreateClient(Action<IServiceCollection>? extraConfig = null)
    {
        var factory = new HangfireBypassFactory().WithWebHostBuilder(builder =>
        {
            // Tests never connect to a real Redis — supply a placeholder so
            // AddRateLimiting's null-check passes during host build.
            builder.UseSetting("Redis:ConnectionString", "localhost:6379,abortConnect=false");

            builder.ConfigureServices(services =>
            {
                // SQLite in-memory for the DbContext (matches other test classes).
                var dbDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (dbDescriptor != null) services.Remove(dbDescriptor);
                services.AddDbContext<AppDbContext>(o => o.UseSqlite("DataSource=:memory:"));

                // Replace the Redis-backed store with the in-memory equivalent.
                var storeDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IRateLimiterStore));
                if (storeDescriptor != null) services.Remove(storeDescriptor);
                services.AddSingleton<IRateLimiterStore, InMemoryRateLimiterStore>();

                // Drop the IConnectionMultiplexer registration entirely so no
                // code path can accidentally trigger an outbound Redis connect.
                var muxDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IConnectionMultiplexer));
                if (muxDescriptor != null) services.Remove(muxDescriptor);

                extraConfig?.Invoke(services);
            });
        });

        return factory.CreateClient();
    }

    #region Header Presence

    [Fact]
    public async Task LimitedEndpoint_ReturnsRateLimitHeaders()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/v1/_diag/ratelimit/public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-RateLimit-Limit"));
        Assert.True(response.Headers.Contains("X-RateLimit-Remaining"));
        Assert.True(response.Headers.Contains("X-RateLimit-Reset"));

        Assert.Equal("30", response.Headers.GetValues("X-RateLimit-Limit").First());
        Assert.Equal("29", response.Headers.GetValues("X-RateLimit-Remaining").First());

        var reset = long.Parse(response.Headers.GetValues("X-RateLimit-Reset").First());
        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.InRange(reset, nowEpoch, nowEpoch + 65); // window = 60s + slack
    }

    [Fact]
    public async Task UnmarkedEndpoint_HasNoRateLimitHeaders()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/v1/_diag/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-RateLimit-Limit"));
        Assert.False(response.Headers.Contains("X-RateLimit-Remaining"));
        Assert.False(response.Headers.Contains("X-RateLimit-Reset"));
    }

    [Fact]
    public async Task RemainingHeader_DecreasesWithEachRequest()
    {
        var client = CreateClient();

        var first = await client.GetAsync("/api/v1/_diag/ratelimit/public");
        var second = await client.GetAsync("/api/v1/_diag/ratelimit/public");
        var third = await client.GetAsync("/api/v1/_diag/ratelimit/public");

        Assert.Equal("29", first.Headers.GetValues("X-RateLimit-Remaining").First());
        Assert.Equal("28", second.Headers.GetValues("X-RateLimit-Remaining").First());
        Assert.Equal("27", third.Headers.GetValues("X-RateLimit-Remaining").First());
    }

    #endregion

    #region 429 Responses

    [Fact]
    public async Task ExceedingLimit_Returns429()
    {
        var client = CreateClient();

        // auth policy = 10/min — 11th request must be blocked
        for (var i = 0; i < 10; i++)
        {
            var ok = await client.GetAsync("/api/v1/_diag/ratelimit/auth");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var blocked = await client.GetAsync("/api/v1/_diag/ratelimit/auth");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task BlockedResponse_IncludesRetryAfterHeader()
    {
        var client = CreateClient();

        // steam-inventory = 5/min — quickest path to a 429
        for (var i = 0; i < 5; i++)
        {
            await client.GetAsync("/api/v1/_diag/ratelimit/steam-inventory");
        }

        var blocked = await client.GetAsync("/api/v1/_diag/ratelimit/steam-inventory");

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));

        var retryAfter = int.Parse(blocked.Headers.GetValues("Retry-After").First());
        Assert.InRange(retryAfter, 1, 60);
    }

    [Fact]
    public async Task BlockedResponse_IncludesZeroRemainingHeader()
    {
        var client = CreateClient();

        for (var i = 0; i < 5; i++)
        {
            await client.GetAsync("/api/v1/_diag/ratelimit/steam-inventory");
        }

        var blocked = await client.GetAsync("/api/v1/_diag/ratelimit/steam-inventory");

        Assert.Equal("5", blocked.Headers.GetValues("X-RateLimit-Limit").First());
        Assert.Equal("0", blocked.Headers.GetValues("X-RateLimit-Remaining").First());
    }

    [Fact]
    public async Task BlockedResponse_HasRateLimitErrorEnvelope()
    {
        var client = CreateClient();

        for (var i = 0; i < 5; i++)
        {
            await client.GetAsync("/api/v1/_diag/ratelimit/steam-inventory");
        }

        var blocked = await client.GetAsync("/api/v1/_diag/ratelimit/steam-inventory");
        var body = await DeserializeResponse(blocked);

        Assert.False(body.Success);
        Assert.NotNull(body.Error);
        Assert.Equal("RATE_LIMIT_EXCEEDED", body.Error!.Code);
        Assert.NotNull(body.TraceId);
    }

    #endregion

    #region Policy Isolation

    [Fact]
    public async Task DifferentPolicies_HaveIndependentLimits()
    {
        var client = CreateClient();

        // public = 30/min, auth = 10/min — exhausting auth must not affect public
        for (var i = 0; i < 10; i++)
        {
            await client.GetAsync("/api/v1/_diag/ratelimit/auth");
        }

        var authBlocked = await client.GetAsync("/api/v1/_diag/ratelimit/auth");
        Assert.Equal(HttpStatusCode.TooManyRequests, authBlocked.StatusCode);

        var publicResponse = await client.GetAsync("/api/v1/_diag/ratelimit/public");
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        Assert.Equal("30", publicResponse.Headers.GetValues("X-RateLimit-Limit").First());
    }

    [Fact]
    public async Task DifferentPolicies_AdvertiseTheirOwnLimits()
    {
        var client = CreateClient();

        var auth = await client.GetAsync("/api/v1/_diag/ratelimit/auth");
        var publicResp = await client.GetAsync("/api/v1/_diag/ratelimit/public");
        var userRead = await client.GetAsync("/api/v1/_diag/ratelimit/user-read");
        var steam = await client.GetAsync("/api/v1/_diag/ratelimit/steam-inventory");

        Assert.Equal("10", auth.Headers.GetValues("X-RateLimit-Limit").First());
        Assert.Equal("30", publicResp.Headers.GetValues("X-RateLimit-Limit").First());
        Assert.Equal("60", userRead.Headers.GetValues("X-RateLimit-Limit").First());
        Assert.Equal("5", steam.Headers.GetValues("X-RateLimit-Limit").First());
    }

    #endregion

    #region Disabled

    [Fact]
    public async Task RateLimitDisabled_BypassesMiddleware()
    {
        var client = new HangfireBypassFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Redis:ConnectionString", "localhost:6379,abortConnect=false");
            builder.UseSetting("RateLimit:Enabled", "false");

            builder.ConfigureServices(services =>
            {
                var dbDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (dbDescriptor != null) services.Remove(dbDescriptor);
                services.AddDbContext<AppDbContext>(o => o.UseSqlite("DataSource=:memory:"));

                var storeDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IRateLimiterStore));
                if (storeDescriptor != null) services.Remove(storeDescriptor);
                services.AddSingleton<IRateLimiterStore, InMemoryRateLimiterStore>();

                var muxDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IConnectionMultiplexer));
                if (muxDescriptor != null) services.Remove(muxDescriptor);
            });
        }).CreateClient();

        // 100 requests should all succeed when the limiter is off
        for (var i = 0; i < 50; i++)
        {
            var resp = await client.GetAsync("/api/v1/_diag/ratelimit/steam-inventory");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.False(resp.Headers.Contains("X-RateLimit-Limit"));
        }
    }

    #endregion

    #region Helpers

    private static async Task<TestApiResponse> DeserializeResponse(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TestApiResponse>(content, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize response: {content}");
    }

    private record TestApiResponse(
        bool Success,
        JsonElement? Data,
        TestApiError? Error,
        string? TraceId);

    private record TestApiError(
        string Code,
        string Message,
        JsonElement? Details);

    #endregion
}
