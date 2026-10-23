using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Skinora.API.HealthChecks;

/// <summary>
/// T165 — body of the public <c>/api/health</c> endpoint read by outside
/// monitors (DEPLOY_RUNBOOK §P). Only the overall status leaves the server:
/// the detailed <see cref="HealthCheckResponseWriter"/> names every check and
/// copies exception messages (a SQL connection error can carry a host name),
/// which is fine on the compose network and not on the internet.
/// </summary>
public static class PublicHealthResponseWriter
{
    /// <summary>Route of the public endpoint (behind nginx's <c>/api/</c> location).</summary>
    public const string Path = "/api/health";

    public static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        var status = report.Status switch
        {
            HealthStatus.Healthy => "healthy",
            HealthStatus.Degraded => "degraded",
            _ => "unhealthy",
        };
        return context.Response.WriteAsync($"{{\"status\":\"{status}\"}}");
    }
}
