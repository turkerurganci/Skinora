using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;
using Skinora.API.BackgroundJobs;
using Skinora.API.BackgroundJobs.Timeouts;
using Skinora.API.Configuration;
using Skinora.API.Filters;
using Skinora.API.Logging;
using Skinora.API.Middleware;
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Startup;
using Skinora.Admin;
using Skinora.Admin.Infrastructure.Persistence;
using Skinora.Auth.Infrastructure.Persistence;
using Skinora.Disputes.Infrastructure.Persistence;
using Skinora.Fraud;
using Skinora.Fraud.Infrastructure.Persistence;
using Skinora.Notifications;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Payments.Infrastructure.Persistence;
using Skinora.Platform;
using Skinora.Platform.Infrastructure.Bootstrap;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Persistence;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Infrastructure.Persistence;

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
            sqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
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

// Steam OpenID authentication services (T29)
builder.Services.AddSteamAuthenticationModule(builder.Configuration);

// User profile + wallet + account settings (T33 / T34 / T35) —
// /users/me, /users/me/stats, /users/:steamId, /users/me/wallet/*, /users/me/settings/*
builder.Services.AddUsersModule(builder.Configuration);

// Admin role + user management (T39 — 07 §9.11–§9.18). Permission claim
// issuance arrives with T40; until then the dynamic Permission:* policies
// only succeed for super-admins via PermissionAuthorizationHandler bypass.
builder.Services.AddAdminModule();

// Fraud flag lifecycle (T54 — 02 §14.0, 07 §9.2–§9.5). Admin review queue
// + auto-EMERGENCY_HOLD cascade for high-risk account flags. The
// pre-create flag writer registered here is consumed by
// TransactionCreationService (FLAGGED → matching FraudFlag row).
builder.Services.AddFraudModule();

// Platform parameter management (T41 — 07 §9.8–§9.9). ISystemSettingsService
// reads the SystemSetting catalog and applies type/range/cross-key validation
// to admin updates. Audit rows write directly to AuditLogs pending T42's
// centralised pipeline. T47 binds Heartbeat options + IHeartbeatJob.
builder.Services.Configure<Skinora.Platform.Application.Heartbeat.HeartbeatOptions>(
    builder.Configuration.GetSection(Skinora.Platform.Application.Heartbeat.HeartbeatOptions.SectionName));
builder.Services.AddPlatformModule();

// Notification infrastructure (T37 — 05 §7.1–§7.5): dispatcher orchestration,
// .resx-backed template resolver, per-channel handlers (Email/Telegram/Discord
// stubs swapped at T78/T79/T80), exponential-backoff Hangfire delivery job and
// admin-alert sink for exhausted retries.
builder.Services.AddNotificationsModule();

// Transaction lifecycle (T45 — 07 §7.2–§7.4, 03 §2.2): eligibility,
// params and creation services. Steam inventory + market price ports are
// registered as forward-deferred stubs (T67/T81 swap them via DI). T47
// adds timeout scheduling (per-tx Hangfire jobs + deadline scanner).
builder.Services.AddTransactionsModule(builder.Configuration);

// T47 — restart recovery + startup hook for the heartbeat / scanner chains.
// Order: registered AFTER the outbox hook so the recovery pass observes a
// settled DB. Hosted services run in registration order (StartAsync sequence).
builder.Services.AddScoped<IRestartRecoveryService, RestartRecoveryService>();

// Rate limiting (T07) — Redis-backed fixed window, opt-in via [RateLimit] attribute
builder.Services.AddRateLimiting(builder.Configuration);

// Hangfire (T09) — SQL Server storage, UTC, AutomaticRetry(3) global filter,
// IBackgroundJobScheduler abstraction. Dashboard mount happens later in the
// pipeline (after authentication) via app.UseHangfireModule().
builder.Services.AddHangfireModule(builder.Configuration);

// T26 — SystemSetting bootstrap (env var hydration + startup fail-fast,
// 06 §8.9). Registered before the outbox hook so the dispatcher chain only
// primes once configuration is proven complete. IHostedService StartAsync
// order follows registration order.
builder.Services.AddScoped<SettingsBootstrapService>();
builder.Services.AddHostedService<SettingsBootstrapHook>();

// Outbox (T10) — IOutboxService producer, dispatcher (self-rescheduling
// Hangfire job + Medallion distributed lock), consumer idempotency store,
// receiver-side external idempotency service, MediatR fan-out and the
// startup hook that primes the dispatcher chain.
builder.Services.AddOutboxModule(builder.Configuration);

// T47 — primes the restart-recovery + heartbeat + deadline scanner chains
// at host startup. Registered after Outbox so its hosted-service StartAsync
// runs after the outbox dispatcher chain is alive.
builder.Services.AddHostedService<TimeoutSchedulerStartupHook>();

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
})
.AddJsonOptions(options =>
{
    // T45 — accept enum names ("USDT", "STEAM_ID", "CREATED") on inbound
    // request bodies and emit them on responses, matching the 07 contract.
    options.JsonSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// Module entity registrations (T18+) — register module assemblies so their
// IEntityTypeConfiguration<T> implementations are discovered by AppDbContext.
UsersModuleDbRegistration.RegisterUsersModule();
AuthModuleDbRegistration.RegisterAuthModule();
TransactionsModuleDbRegistration.RegisterTransactionsModule();
SteamModuleDbRegistration.RegisterSteamModule();
DisputesModuleDbRegistration.RegisterDisputesModule();
FraudModuleDbRegistration.RegisterFraudModule();
NotificationsModuleDbRegistration.RegisterNotificationsModule();
AdminModuleDbRegistration.RegisterAdminModule();
PaymentsModuleDbRegistration.RegisterPaymentsModule();
PlatformModuleDbRegistration.RegisterPlatformModule();

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
