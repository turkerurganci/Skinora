using Microsoft.EntityFrameworkCore;
using Prometheus;
using Serilog;
using Skinora.API.BackgroundJobs;
using Skinora.API.BackgroundJobs.Timeouts;
using Skinora.API.Configuration;
using Skinora.API.Filters;
using Skinora.API.Logging;
using Skinora.API.Middleware;
using Skinora.API.Monitoring;
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Retention;
using Skinora.API.Startup;
using Skinora.Admin;
using Skinora.Admin.Infrastructure.Persistence;
using Skinora.Auth.Infrastructure.Persistence;
using Skinora.Disputes;
using Skinora.Disputes.Infrastructure.Persistence;
using Skinora.Fraud;
using Skinora.Fraud.Infrastructure.Persistence;
using Skinora.Notifications;
using Skinora.Notifications.Infrastructure.Persistence;
using Skinora.Payments.Infrastructure.Persistence;
using Skinora.Platform;
using Skinora.Platform.Infrastructure.Bootstrap;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.API.Services;
using Skinora.Realtime;
using Skinora.Realtime.Hubs;
using Skinora.Shared.Discord;
using Skinora.Shared.Email;
using Skinora.Shared.Persistence;
using Skinora.Shared.SteamMarket;
using Skinora.Shared.Telegram;
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

// T79 — Telegram bot transport (08 §5.1–§5.5). TelegramSettings drives
// both the connection service (Skinora.Users → /start deep-link
// handler) and the notification channel handler
// (Skinora.Notifications → outbound sendMessage). Registering the
// options + rate limiter + HttpClient here before the module wiring
// lets per-module composition pick the right concrete based on the
// provider flag. The HttpClient is only registered for the telegram
// provider so a misconfigured stub-mode build cannot accidentally
// reach the network.
builder.Services.Configure<TelegramSettings>(
    builder.Configuration.GetSection(TelegramSettings.SectionName));
builder.Services.AddSingleton<ITelegramRateLimiter, TelegramRateLimiter>();

var telegramProvider = builder.Configuration[$"{TelegramSettings.SectionName}:{nameof(TelegramSettings.Provider)}"]
    ?? TelegramSettings.ProviderLogging;
if (string.Equals(telegramProvider, TelegramSettings.ProviderTelegram, StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<ITelegramBotClient, TelegramBotClient>((sp, client) =>
    {
        var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TelegramSettings>>().Value;
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
    });
}

// T80 — Discord transport (08 §6.1–§6.5). DiscordSettings drives
// both the OAuth callback (Skinora.Users → /users/me/settings/discord/callback)
// and the notification DM channel handler (Skinora.Notifications →
// createDM + sendMessage). Two typed HttpClients: OAuth uses Bearer
// access_tokens, Bot uses the "Bot {token}" authorization header — the
// concrete clients set those headers themselves so the registrations
// stay symmetric. Both clients are only registered when
// Discord:Provider == "discord", so a misconfigured stub-mode build
// cannot accidentally reach the network.
builder.Services.Configure<DiscordSettings>(
    builder.Configuration.GetSection(DiscordSettings.SectionName));
builder.Services.AddSingleton<IDiscordRateLimiter, DiscordRateLimiter>();

var discordProvider = builder.Configuration[$"{DiscordSettings.SectionName}:{nameof(DiscordSettings.Provider)}"]
    ?? DiscordSettings.ProviderLogging;
if (string.Equals(discordProvider, DiscordSettings.ProviderDiscord, StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IDiscordOAuthClient, DiscordOAuthClient>((sp, client) =>
    {
        var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DiscordSettings>>().Value;
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
    });
    builder.Services.AddHttpClient<IDiscordBotClient, DiscordBotClient>((sp, client) =>
    {
        var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DiscordSettings>>().Value;
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
    });
}

// T81 — Steam Market price API transport (08 §7.1–§7.4). The shared
// settings + sliding-window rate limiter live across every consumer; the
// real HttpClient is wired only when Provider == "steam-market" so a
// fresh checkout / CI build cannot hit steamcommunity.com by accident.
// Provider == "logging" (default) resolves a NoPrice stub instead, which
// makes the fraud price-deviation pipeline degrade per 08 §7.4 karar
// ağacı adım 3b without producing real outbound traffic.
builder.Services.Configure<SteamMarketSettings>(
    builder.Configuration.GetSection(SteamMarketSettings.SectionName));
builder.Services.AddSingleton<ISteamMarketRateLimiter, SteamMarketRateLimiter>();

var steamMarketProvider = builder.Configuration[$"{SteamMarketSettings.SectionName}:{nameof(SteamMarketSettings.Provider)}"]
    ?? SteamMarketSettings.ProviderLogging;
if (string.Equals(steamMarketProvider, SteamMarketSettings.ProviderSteamMarket, StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<ISteamMarketPriceClient, SteamMarketPriceClient>((sp, client) =>
    {
        var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SteamMarketSettings>>().Value;
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
    });
}
else
{
    builder.Services.AddSingleton<ISteamMarketPriceClient, LoggingSteamMarketPriceClient>();
}

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

// T82 — Admin sanctions list management (07 §9.23–§9.25 AD22/AD23/AD24,
// 02 §21.1, 03 §11a.3). Cross-module orchestrator (Skinora.API/Services/
// AdminSanctions): SanctionedAddress CRUD + AuditLog + retroaktif eşleşme
// cascade via ISanctionsViolationHandler. Registration sits after
// AddFraudModule so ISanctionsViolationHandler is wired before consumption.
builder.Services.AddScoped<
    Skinora.API.Services.AdminSanctions.IAdminSanctionsService,
    Skinora.API.Services.AdminSanctions.AdminSanctionsService>();

// Platform parameter management (T41 — 07 §9.8–§9.9). ISystemSettingsService
// reads the SystemSetting catalog and applies type/range/cross-key validation
// to admin updates. Audit rows write directly to AuditLogs pending T42's
// centralised pipeline. T47 binds Heartbeat options + IHeartbeatJob.
builder.Services.Configure<Skinora.Platform.Application.Heartbeat.HeartbeatOptions>(
    builder.Configuration.GetSection(Skinora.Platform.Application.Heartbeat.HeartbeatOptions.SectionName));
builder.Services.AddPlatformModule();

// T78 — Resend email transport (08 §4.1–§4.3). ResendSettings drives both
// the notification email channel handler (Skinora.Notifications) and the
// verification-email sender (Skinora.Users); registering the options +
// HttpClient + Svix verifier here before the module wiring lets the
// per-module composition pick the right concrete based on the provider
// flag. The HttpClient is only registered for the Resend provider so a
// misconfigured stub-mode build cannot accidentally reach the network.
builder.Services.Configure<ResendSettings>(
    builder.Configuration.GetSection(ResendSettings.SectionName));
builder.Services.AddSingleton<SvixSignatureVerifier>();

var resendProvider = builder.Configuration[$"{ResendSettings.SectionName}:{nameof(ResendSettings.Provider)}"]
    ?? ResendSettings.ProviderLogging;
if (string.Equals(resendProvider, ResendSettings.ProviderResend, StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IResendEmailClient, ResendEmailClient>((sp, client) =>
    {
        var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ResendSettings>>().Value;
        client.BaseAddress = new Uri(settings.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);
    });
}

// Notification infrastructure (T37 — 05 §7.1–§7.5): dispatcher orchestration,
// .resx-backed template resolver, per-channel handlers (Email/Telegram/Discord
// stubs swapped at T78/T79/T80), exponential-backoff Hangfire delivery job and
// admin-alert sink for exhausted retries. T78 added the deferred-tier
// delivery job + Resend webhook handler + HTML wrapper.
builder.Services.AddNotificationsModule(builder.Configuration);

// Transaction lifecycle (T45 — 07 §7.2–§7.4, 03 §2.2): eligibility,
// params and creation services. Steam inventory + market price ports are
// registered as forward-deferred stubs (T67/T81 swap them via DI). T47
// adds timeout scheduling (per-tx Hangfire jobs + deadline scanner).
builder.Services.AddTransactionsModule(builder.Configuration);

// Buyer-facing dispute pipeline (T58 — 02 §10, 03 §6, 07 §7.8–§7.10).
// Three endpoints (open / submit-txhash / escalate) backed by
// IDisputeService + per-type auto-checkers (PAYMENT/DELIVERY/WRONG_ITEM).
builder.Services.AddDisputesModule();

// Steam bot read service (T63 — 07 §9.10 AD10). Sidecar wiring + bot
// failover land with T64–T69 and will register here too.
builder.Services.AddSteamModule(builder.Configuration);

// T63 — admin dashboard composer (07 §9.1 AD1). Composes summary counters,
// the AD10 Steam-bot snapshot and the latest fraud flags in one round-trip.
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();

// WP5 / T58 — admin dispute resolution (AD27–AD29, 07 §9.x). Closes the
// ESCALATED dead-end; orchestrates Disputes + Transactions (state machine /
// refund events) + Platform (audit) at the composition root.
builder.Services.AddScoped<Skinora.Disputes.Application.Admin.IAdminDisputeService, AdminDisputeService>();

// T63a — public /platform endpoints (07 §10.1 P1 stats, §10.2 P2 maintenance).
// IMemoryCache is sufficient for these read paths: stats data drifts slowly
// (15 min TTL) and maintenance toggles propagate within 30 s through the
// per-replica cache; cross-replica invalidation is unnecessary at this scale.
builder.Services.AddMemoryCache();
builder.Services.Configure<PlatformOptions>(
    builder.Configuration.GetSection(PlatformOptions.SectionName));
builder.Services.AddScoped<IPlatformPublicService, PlatformPublicService>();

// WP7 — admin maintenance/outage control (07 §9.31). Spans Platform settings,
// Transactions timeout-freeze and Realtime push, so it lives at the API
// composition root alongside the public maintenance read service.
builder.Services.AddScoped<IAdminMaintenanceService, AdminMaintenanceService>();

// T61 / T62 — SignalR hubs + realtime publishers (07 §11.1 RT1
// /hubs/transactions, 07 §11.2 RT2 /hubs/notifications) + the CountdownSync
// 30s broadcaster. MediatR consumers in the Realtime assembly are picked up
// by the outbox dispatcher's scan list. The JSON protocol uses string enum
// names so the wire format matches the spec payload tables (e.g.
// "CANCELLED_BUYER" rather than the integer ordinal).
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddRealtimeModule(builder.Configuration);

// T47 — restart recovery + startup hook for the heartbeat / scanner chains.
// Order: registered AFTER the outbox hook so the recovery pass observes a
// settled DB. Hosted services run in registration order (StartAsync sequence).
builder.Services.AddScoped<IRestartRecoveryService, RestartRecoveryService>();

// Rate limiting (T07) — Redis-backed fixed window, opt-in via [RateLimit] attribute
builder.Services.AddRateLimiting(builder.Configuration);

// T68 — webhook signature parameters (HMAC-SHA256 shared secret + replay
// window). The Steam sidecar reads the same secret from its WEBHOOK_SECRET
// env var (sidecar-steam/src/config/index.ts).
builder.Services.Configure<WebhookSettings>(
    builder.Configuration.GetSection(WebhookSettings.SectionName));

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

// WP16 (05 §4.4 step 3) — start the Hangfire processing server AFTER the
// restart-recovery hook above. Hosted-service StartAsync runs in registration
// order, so on a cold start following an outage the recovery pass extends all
// active deadlines and re-issues the per-tx jobs BEFORE any worker begins
// draining the queue. This is the explicit "resume processing only after the
// extension completes" gate: overdue jobs queued in SQL Server cannot fire
// against stale deadlines during the recovery window. The Hangfire client
// (IBackgroundJobScheduler) was already registered in AddHangfireModule so the
// priming hooks above could enqueue while the worker is held back.
builder.Services.AddHangfireProcessingServer(builder.Configuration);

// T63b — retention recurring jobs (06 §1, §3.18, §3.19, §3.21, §6.1):
// outbox tables (daily 03:30 UTC), orphan notifications + user login logs
// (weekly Sunday 04:00/04:30 UTC). Each job reads its retention window and
// batch size from SystemSettings on every run so admin tuning takes effect
// without a redeploy.
builder.Services.AddScoped<OutboxRetentionCleanupJob>();
builder.Services.AddScoped<OrphanNotificationRetentionCleanupJob>();
builder.Services.AddScoped<UserLoginLogRetentionCleanupJob>();
builder.Services.AddScoped<ProcessedNonceCleanupJob>();
builder.Services.AddHostedService<RetentionJobsRegistrar>();

// WP16 — platform health probe (05 §4.4, 02 §3.3). Periodic Hangfire job sweeps
// the Steam + blockchain sidecar /health endpoints and raises an admin alert
// (in-app + audit) on each outage / recovery transition. Alert-only — the admin
// applies the maintenance freeze (WP7) if warranted. Single-instance MVP state
// (Redis scale-out is post-MVP, PRE_F6_PLAN §3).
builder.Services.Configure<HealthProbeOptions>(
    builder.Configuration.GetSection(HealthProbeOptions.SectionName));
builder.Services.AddSingleton<PlatformHealthMonitorState>();
builder.Services.AddHttpClient(SidecarHealthClient.HttpClientName, client =>
{
    // Tight timeout — a stuck sidecar must surface as an outage, not hang the
    // probe sweep.
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddScoped<ISidecarHealthClient, SidecarHealthClient>();
builder.Services.AddScoped<IPlatformHealthProbeJob, PlatformHealthProbeJob>();
builder.Services.AddHostedService<HealthProbeRegistrar>();

// Account suspension (T105a) — admin suspend/unsuspend service + the temp-block
// auto-unsuspend recurring job (lifts expired suspensions every 6h).
builder.Services.AddScoped<Skinora.API.Services.UserSuspension.IAdminUserSuspensionService,
    Skinora.API.Services.UserSuspension.AdminUserSuspensionService>();
builder.Services.AddScoped<Skinora.API.Services.UserSuspension.AutoUnsuspendJob>();
builder.Services.AddHostedService<Skinora.API.Services.UserSuspension.AutoUnsuspendJobRegistrar>();

// Multi-account retro-scan (WP4b) — daily sweep that re-runs IMultiAccountDetector
// across wallet-bearing active users, closing the "only fires at wallet-update" gap.
builder.Services.AddScoped<Skinora.API.Services.Fraud.MultiAccountRetroScanJob>();
builder.Services.AddHostedService<Skinora.API.Services.Fraud.MultiAccountRetroScanJobRegistrar>();

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

// 5a. Webhook signature verification (T68 — 05 §3.4, 09 §11.3). Path-scoped
// to /api/v1/webhooks/steam so the legacy Telegram webhook keeps its own
// secret-header check. Runs after CorrelationId/Logging/Exception so a 401
// here is still correlated and gracefully reported.
app.UseMiddleware<WebhookSignatureMiddleware>();

// 5b. Resend webhook signature verification (T78 — 08 §4.3, Svix-style).
// Path-scoped to /api/v1/webhooks/resend; runs before MVC routing so the
// controller never sees an unsigned / replayed / duplicate event.
app.UseMiddleware<ResendWebhookSignatureMiddleware>();

// 5c. Telegram webhook secret-token + update_id idempotency (T79 —
// 08 §5.2). Path-scoped to /api/v1/webhooks/telegram; the controller
// runs only after the secret check and dedup gate have passed.
app.UseMiddleware<TelegramWebhookSignatureMiddleware>();

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
app.MapHub<TransactionsHub>("/hubs/transactions"); // T61 — 07 §11.1 RT1
app.MapHub<NotificationsHub>("/hubs/notifications"); // T62 — 07 §11.2 RT2
app.MapMetrics(); // /metrics endpoint for Prometheus
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = Skinora.API.HealthChecks.HealthCheckResponseWriter.WriteResponse
});

app.Run();

// Required for integration test WebApplicationFactory access
public partial class Program;
