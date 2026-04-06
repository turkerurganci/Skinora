using Microsoft.EntityFrameworkCore;
using Serilog;
using Skinora.API.Configuration;
using Skinora.API.Filters;
using Skinora.API.Middleware;
using Skinora.API.RateLimiting;
using Skinora.Shared.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration)
          .Enrich.FromLogContext());

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

// 12. Endpoints
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "skinora-backend" }));

app.Run();

// Required for integration test WebApplicationFactory access
public partial class Program;
