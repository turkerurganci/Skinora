using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Skinora.API.HealthChecks;

/// <summary>
/// Writes a structured JSON response for the /health endpoint (T16 — 05 §9.5).
/// </summary>
public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            service = "skinora-backend",
            uptime = Environment.TickCount64 / 1000.0,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString().ToLowerInvariant(),
                duration = e.Value.Duration.TotalMilliseconds,
                message = e.Value.Description ?? e.Value.Exception?.Message
            })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }
}
