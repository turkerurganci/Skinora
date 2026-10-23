using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Skinora.API.Tests.Common;
using Skinora.Shared.Persistence;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T165 — the public <c>/api/health</c> endpoint the outside monitors read
/// (DEPLOY_RUNBOOK §P): overall status only, no check names, no exception
/// text; 503 when a dependency check fails; never cached. The detailed
/// <c>/health</c> keeps its body for internal callers (contrast test).
/// </summary>
public class PublicHealthEndpointTests : IClassFixture<HangfireBypassFactory>
{
    private const string InternalDetail = "Server=db-internal-host;User=sa";

    private readonly HangfireBypassFactory _factory;

    public PublicHealthEndpointTests(HangfireBypassFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(HealthStatus? databaseCheck)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);
                services.AddDbContext<AppDbContext>(options => options.UseSqlite("DataSource=:memory:"));
            });
            builder.ConfigureTestServices(services =>
            {
                if (databaseCheck is { } status)
                {
                    services.AddHealthChecks().AddCheck(
                        "sqlserver",
                        () => new HealthCheckResult(status, description: $"cannot open {InternalDetail}"),
                        tags: ["db", "ready"]);
                }
            });
        }).CreateClient();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Healthy_Returns200_StatusOnly_NoStore()
    {
        var response = await CreateClient(HealthStatus.Healthy).GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"healthy\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Unhealthy_Returns503_WithoutCheckNamesOrMessages()
    {
        var client = CreateClient(HealthStatus.Unhealthy);

        var response = await client.GetAsync("/api/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("{\"status\":\"unhealthy\"}", body);
        Assert.DoesNotContain("sqlserver", body);
        Assert.DoesNotContain("db-internal-host", body);

        // Contrast: the internal /health still names the check and its message —
        // which is why nginx no longer serves it to the internet.
        var detailed = await (await client.GetAsync("/health")).Content.ReadAsStringAsync();
        Assert.Contains("sqlserver", detailed);
        Assert.Contains("db-internal-host", detailed);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task NoAuthenticationNeeded_AndNoApiEnvelope()
    {
        // An outside monitor sends no cookie / token; the body is not wrapped
        // in the API envelope (ApiResponseWrapperFilter covers controllers only).
        var response = await CreateClient(databaseCheck: null).GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"healthy\"}", await response.Content.ReadAsStringAsync());
    }
}
