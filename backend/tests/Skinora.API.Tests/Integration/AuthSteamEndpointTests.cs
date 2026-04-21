using System.Net;
using Medallion.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Linq.Expressions;
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Startup;
using Skinora.API.Tests.Common;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

public class AuthSteamEndpointTests : IClassFixture<AuthSteamEndpointTests.Factory>
{
    private const string SteamId = "76561198999999001";
    private const string ClaimedId = "https://steamcommunity.com/openid/id/" + SteamId;

    private readonly Factory _factory;

    public AuthSteamEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.ResetFakes();
    }

    [Fact]
    public async Task GetSteam_RedirectsToSteamOpenIdLogin()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/v1/auth/steam?returnUrl=/transactions/abc");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://steamcommunity.com/openid/login?",
            response.Headers.Location?.ToString());
        Assert.Contains("openid.mode=checkid_setup", response.Headers.Location!.Query);
        _ = body;
    }

    [Fact]
    public async Task GetSteam_InvalidReturnUrl_IsNotReflectedInRedirect()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/v1/auth/steam?returnUrl=https://evil.com/attack");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://steamcommunity.com/openid/login?",
            response.Headers.Location?.ToString());
        // The state cookie should carry the sanitized default, not the attacker URL.
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith("skinora_oid_rt="))
            : null;
        Assert.NotNull(cookie);
        Assert.Contains("skinora_oid_rt=%2Fdashboard", cookie);
        Assert.DoesNotContain("evil.com", cookie);
    }

    [Fact]
    public async Task Callback_ValidAssertion_NewUser_CreatesUserAndSetsRefreshCookie()
    {
        _factory.ValidatorFake.SteamIdToReturn = SteamId;
        _factory.ValidatorFake.IsValid = true;

        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await SendCallbackAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://localhost:3000/auth/callback", location);
        Assert.Contains("status=new_user", location);

        Assert.Contains(response.Headers.GetValues("Set-Cookie"),
            v => v.StartsWith("refreshToken=") &&
                 v.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
                 v.Contains("path=/api/v1/auth", StringComparison.OrdinalIgnoreCase));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Set<User>().FirstOrDefaultAsync(u => u.SteamId == SteamId);
        Assert.NotNull(user);
        Assert.Single(db.Set<RefreshToken>().Where(t => t.UserId == user!.Id));
        Assert.Single(db.Set<UserLoginLog>().Where(l => l.UserId == user!.Id));
    }

    [Fact]
    public async Task Callback_ValidAssertion_ExistingUser_UpdatesDisplayNameAndReturnsSuccess()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<User>().Add(new User
            {
                Id = Guid.NewGuid(),
                SteamId = SteamId,
                SteamDisplayName = "OldName",
            });
            await db.SaveChangesAsync();
        }

        _factory.ValidatorFake.SteamIdToReturn = SteamId;
        _factory.ValidatorFake.IsValid = true;
        _factory.ProfileFake.SummaryToReturn =
            new SteamPlayerSummary(SteamId, "NewName", "https://cdn.steam/avatar.jpg");

        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await SendCallbackAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("status=success", response.Headers.Location!.ToString());

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await verifyDb.Set<User>().SingleAsync(u => u.SteamId == SteamId);
        Assert.Equal("NewName", user.SteamDisplayName);
        Assert.Equal("https://cdn.steam/avatar.jpg", user.SteamAvatarUrl);
    }

    [Fact]
    public async Task Callback_InvalidAssertion_RedirectsWithAuthFailedAndDoesNotPersistUser()
    {
        _factory.ValidatorFake.IsValid = false;

        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await SendCallbackAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=auth_failed", response.Headers.Location!.ToString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Set<User>().AnyAsync());
        Assert.False(await db.Set<RefreshToken>().AnyAsync());
    }

    [Fact]
    public async Task Callback_DeactivatedUser_RedirectsWithAccountBannedAndDoesNotIssueCookie()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<User>().Add(new User
            {
                Id = Guid.NewGuid(),
                SteamId = SteamId,
                SteamDisplayName = "Banned",
                IsDeactivated = true,
                DeactivatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        _factory.ValidatorFake.SteamIdToReturn = SteamId;
        _factory.ValidatorFake.IsValid = true;

        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await SendCallbackAsync(client);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=account_banned", response.Headers.Location!.ToString());

        var hasRefreshCookie = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(c => c.StartsWith("refreshToken="));
        Assert.False(hasRefreshCookie);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verifyDb.Set<RefreshToken>().AnyAsync());
    }

    private static Task<HttpResponseMessage> SendCallbackAsync(HttpClient client)
    {
        var qs = "openid.mode=id_res" +
                 $"&openid.claimed_id={Uri.EscapeDataString(ClaimedId)}" +
                 $"&openid.identity={Uri.EscapeDataString(ClaimedId)}" +
                 "&openid.assoc_handle=1234567890" +
                 "&openid.sig=abcdef";
        return client.GetAsync($"/api/v1/auth/steam/callback?{qs}");
    }

    public sealed class FakeSteamOpenIdValidator : ISteamOpenIdValidator
    {
        public bool IsValid { get; set; } = true;
        public string SteamIdToReturn { get; set; } = "76561198000000000";

        public Task<SteamOpenIdValidationResult> ValidateAsync(
            IReadOnlyDictionary<string, string> callbackParameters,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(IsValid
                ? SteamOpenIdValidationResult.Success(SteamIdToReturn)
                : SteamOpenIdValidationResult.Failure("fake-failure"));
        }
    }

    public sealed class FakeSteamProfileClient : ISteamProfileClient
    {
        public SteamPlayerSummary? SummaryToReturn { get; set; }

        public Task<SteamPlayerSummary?> GetPlayerSummaryAsync(
            string steamId64, CancellationToken cancellationToken)
            => Task.FromResult(SummaryToReturn);
    }

    private sealed class NoopBackgroundJobScheduler : IBackgroundJobScheduler
    {
        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
            => Guid.NewGuid().ToString("N");
        public string Enqueue<T>(Expression<Action<T>> methodCall)
            => Guid.NewGuid().ToString("N");
        public bool Delete(string jobId) => true;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;

        public FakeSteamOpenIdValidator ValidatorFake { get; } = new();
        public FakeSteamProfileClient ProfileFake { get; } = new();

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public void ResetFakes()
        {
            ValidatorFake.IsValid = true;
            ValidatorFake.SteamIdToReturn = "76561198000000000";
            ProfileFake.SummaryToReturn = null;

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<RefreshToken>().RemoveRange(db.Set<RefreshToken>());
            db.Set<UserLoginLog>().RemoveRange(db.Set<UserLoginLog>());
            db.Set<User>().RemoveRange(db.Set<User>());
            db.SaveChanges();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Give HangfireModule a well-formed but never contacted connection
            // string so host build does not try to open the real SQL Server.
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(local);Database=SkinoraTest;Integrated Security=true;TrustServerCertificate=true");

            // Disable the Hangfire dashboard middleware — without any
            // Hangfire services registered (see scrub below), the mount
            // would otherwise throw at UseHangfireModule().
            builder.UseSetting("Hangfire:DashboardEnabled", "false");

            builder.UseSetting("Jwt:Secret", "integration-jwt-secret-key-minimum-32-characters!!");
            builder.UseSetting("Jwt:Issuer", "skinora");
            builder.UseSetting("Jwt:Audience", "skinora-client");
            builder.UseSetting("Jwt:AccessTokenExpiryMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenExpiryDays", "7");
            builder.UseSetting("Jwt:PreviousSecret", "");

            builder.UseSetting("SteamOpenId:Realm", "https://skinora.test");
            builder.UseSetting("SteamOpenId:ReturnToUrl",
                "https://skinora.test/api/v1/auth/steam/callback");
            builder.UseSetting("SteamOpenId:FrontendCallbackUrl",
                "https://localhost:3000/auth/callback");
            builder.UseSetting("SteamOpenId:DefaultReturnPath", "/dashboard");
            builder.UseSetting("SteamOpenId:WebApiKey", "");

            builder.ConfigureServices(services =>
            {
                // --- EF Core — strip SqlServer stack and re-add SQLite ---
                var efDescriptors = services
                    .Where(d =>
                        d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                        d.ServiceType == typeof(DbContextOptions) ||
                        d.ServiceType == typeof(AppDbContext) ||
                        (d.ServiceType.IsGenericType &&
                         d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration")) ||
                        (d.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore",
                            StringComparison.Ordinal) ?? false))
                    .ToList();
                foreach (var d in efDescriptors) services.Remove(d);

                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite(_connection));

                // --- Hangfire scrub (keeps JobStorage.Current intact for
                // sibling test classes that DO need Hangfire — we skip the
                // InMemory re-registration HangfireBypassFactory performs
                // precisely to avoid the ObjectDisposedException that
                // otherwise spreads to HangfireTests when this factory is
                // torn down before theirs is built) ---
                var hangfireDescriptors = services
                    .Where(d =>
                        (d.ServiceType.Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) ?? false) ||
                        (d.ImplementationType?.Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) ?? false) ||
                        (d.ImplementationFactory?.Method.DeclaringType?.Assembly.GetName().Name?
                            .StartsWith("Hangfire", StringComparison.Ordinal) ?? false))
                    .ToList();
                foreach (var d in hangfireDescriptors) services.Remove(d);

                // --- Startup hooks that reach for real infra ---
                var startupHookDescriptors = services
                    .Where(d =>
                        d.ImplementationType == typeof(OutboxStartupHook) ||
                        d.ImplementationType == typeof(SettingsBootstrapHook))
                    .ToList();
                foreach (var d in startupHookDescriptors) services.Remove(d);

                // --- IBackgroundJobScheduler (depends on Hangfire) → no-op ---
                services.RemoveAll<IBackgroundJobScheduler>();
                services.AddSingleton<IBackgroundJobScheduler, NoopBackgroundJobScheduler>();

                // --- Medallion distributed lock (SQL-backed) ---
                services.RemoveAll<IDistributedLockProvider>();
                services.AddSingleton<IDistributedLockProvider, InMemoryDistributedLockProvider>();

                // --- Health checks (SQL Server + Redis) ---
                var healthCheckDescriptors = services
                    .Where(d => d.ServiceType.FullName?.Contains("HealthCheck",
                        StringComparison.Ordinal) == true)
                    .ToList();
                foreach (var d in healthCheckDescriptors) services.Remove(d);
                services.AddHealthChecks();

                // --- Rate limiter (Redis) → InMemory ---
                services.RemoveAll<IRateLimiterStore>();
                services.AddSingleton<IRateLimiterStore, InMemoryRateLimiterStore>();

                // --- Steam fakes ---
                services.RemoveAll<ISteamOpenIdValidator>();
                services.RemoveAll<ISteamProfileClient>();
                services.AddSingleton<ISteamOpenIdValidator>(ValidatorFake);
                services.AddSingleton<ISteamProfileClient>(ProfileFake);
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = base.CreateHost(builder);
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            return host;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _connection.Dispose();
            }
        }
    }
}

