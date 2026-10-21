using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skinora.API.Tests.Common;
using Skinora.Shared.Persistence;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T166 — <c>/api/v1/_diag/*</c> exists only in the Development environment.
/// Every other environment name (Production, Staging, a typo) must not
/// register the routes at all, so the request is a plain 404 regardless of
/// method or credentials.
/// </summary>
public class DiagnosticsEnvironmentGateTests : IClassFixture<HangfireBypassFactory>
{
    private readonly HangfireBypassFactory _factory;

    public DiagnosticsEnvironmentGateTests(HangfireBypassFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("production")] // environment names are case-insensitive in ASP.NET Core
    public async Task Diag_OutsideDevelopment_Returns404(string environment)
    {
        using var client = CreateClient(environment);

        foreach (var path in new[]
                 {
                     "/api/v1/_diag/ping",
                     "/api/v1/_diag/throw/unhandled",
                     "/api/v1/_diag/auth/public",
                     "/api/v1/_diag/ratelimit/public",
                 })
        {
            var response = await client.GetAsync(path);
            Assert.True(
                response.StatusCode == HttpStatusCode.NotFound,
                $"{environment}: {path} returned {(int)response.StatusCode}, expected 404");
        }
    }

    [Fact]
    public async Task Diag_InDevelopment_IsReachable()
    {
        // Negative control: the same request IS served in Development, so the
        // 404 above comes from the environment gate and not from a broken route.
        using var client = CreateClient("Development");

        var response = await client.GetAsync("/api/v1/_diag/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OtherControllers_OutsideDevelopment_StillRouted()
    {
        // The convention removes only [DevelopmentOnly] controllers. Any other
        // controller route must stay registered in Production. The test host
        // has no Redis/SQL behind it, so the status itself is not the point —
        // only that the route was matched (anything but 404).
        using var client = CreateClient("Production");

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateClient(string environment) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite("DataSource=:memory:"));
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
