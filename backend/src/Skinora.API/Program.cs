using Microsoft.EntityFrameworkCore;
using Serilog;
using Skinora.API.Configuration;
using Skinora.API.Filters;
using Skinora.API.Middleware;
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

// 8. Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// 9. Anti-forgery
app.UseAntiforgery();

// 10. Endpoints
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "skinora-backend" }));

app.Run();

// Required for integration test WebApplicationFactory access
public partial class Program;
