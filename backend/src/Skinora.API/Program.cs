using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;
using Skinora.API.BackgroundJobs;
using Skinora.API.Configuration;
using Skinora.API.Filters;
using Skinora.API.Logging;
using Skinora.API.Middleware;
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.Shared.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Serilog (T08 — sinks/format/labels driven by appsettings.json; secret masking
// applied centrally via SecretMaskingEnricher per 09 §18.5)
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration)
          .Enrich.FromLogContext()
          .Enrich.With<SecretMaskingEnricher>());

// DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.MigrationsAssembly(typeof(Program).Assembly.GetName().Name);
            sqlOptions.CommandTimeout(30);
        }));

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                             ?? ["https://localhost:3000"];

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Anti-forgery (CSRF)
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "XSRF-TOKEN";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true;
});

// Authentication & Authorization (T06)
builder.Services.AddAuthModule(builder.Configuration);

// Rate limiting (T07) — Redis-backed fixed window, opt-in via [RateLimit] attribute
builder.Services.AddRateLimiting(builder.Configuration);

// Hangfire (T09) — SQL Server storage, UTC, AutomaticRetry(3) global filter,
// IBackgroundJobScheduler abstraction. Dashboard mount happens later in the
// pipeline (after authentication) via app.UseHangfireModule().
builder.Services.AddHangfireModule(builder.Configuration);

// Outbox (T10) — IOutboxService producer, dispatcher (self-rescheduling
// Hangfire job + Medallion distributed lock), consumer idempotency store,
// receiver-side external idempotency service, MediatR fan-out and the
// startup hook that primes the dispatcher chain.
builder.Services.AddOutboxModule(builder.Configuration);

// Health checks (T16) — DB + Redis dependency checks
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "sqlserver",
        tags: ["db", "ready"])
    .AddRedis(
        builder.Configuration["Redis:ConnectionString"]!,
        name: "redis",
        tags: ["cache", "ready"]);

// Controllers + ApiResponseWrapperFilter
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiResponseWrapperFilter>();
});

var app = builder.Build();

// --- Middleware Pipeline (order matters) ---

// 1. HTTPS redirection
app.UseHttpsRedirection();

// 2. Security headers (CSP, X-Content-Type-Options, etc.)
app.UseMiddleware<SecurityHeadersMiddleware>();

// 3. Correlation ID (early — so all logs and responses include it)
app.UseMiddleware<CorrelationIdMiddleware>();

// 4. Serilog request logging
app.UseSerilogRequestLogging();

// 5. Global exception handler (wraps everything downstream)
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 6. CORS
app.UseCors();

// 7. Routing
app.UseRouting();

// 8. Authentication
app.UseAuthentication();

// 9. Rate limiting (after auth so user-scoped policies see the user ID,
//    before authorization so blocked requests skip permission checks)
app.UseMiddleware<RateLimitMiddleware>();

// 10. Authorization
app.UseAuthorization();

// 11. Anti-forgery
app.UseAntiforgery();

// 12. Hangfire dashboard (admin-gated, mounted after auth/authorization so the
//     dashboard authorization filter sees the authenticated principal — T09).
app.UseHangfireModule();

// 13. Prometheus metrics (T16) — exposes /metrics for Prometheus scraping
app.UseHttpMetrics();

// 14. Endpoints
app.MapControllers();
app.MapMetrics(); // /metrics endpoint for Prometheus
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = Skinora.API.HealthChecks.HealthCheckResponseWriter.WriteResponse
});

app.Run();

// Required for integration test WebApplicationFactory access
public partial class Program;
