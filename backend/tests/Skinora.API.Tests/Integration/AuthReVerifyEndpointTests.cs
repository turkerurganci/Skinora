using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Medallion.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Startup;
using Skinora.API.Tests.Common;
using Skinora.Auth.Application.MobileAuthenticator;
using Skinora.Auth.Application.ReAuthentication;
using Skinora.Auth.Application.SteamAuthentication;
using Skinora.Auth.Configuration;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Persistence;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

public class AuthReVerifyEndpointTests : IClassFixture<AuthReVerifyEndpointTests.Factory>
{
    private const string TestSecret = "reverify-test-secret-key-minimum-32-chars!!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string SteamId = "76561198111222333";
    private const string ReVerifyReturnTo =
        "https://skinora.test/api/v1/auth/steam/re-verify/callback";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public AuthReVerifyEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task InitiateReVerify_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/steam/re-verify",
            new { purpose = "wallet_change", returnUrl = "/profile" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InitiateReVerify_Authenticated_ReturnsSteamUrlAndSetsStateCookie()
    {
        var userId = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(userId, SteamId);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/steam/re-verify",
            new { purpose = "wallet_change", returnUrl = "/profile" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        var url = data.GetProperty("steamAuthUrl").GetString();
        Assert.NotNull(url);
        Assert.StartsWith("https://steamcommunity.com/openid/login?", url);
        Assert.Contains("openid.return_to=" + Uri.EscapeDataString(ReVerifyReturnTo), url);

        var setCookie = response.Headers.GetValues("Set-Cookie");
        Assert.Contains(setCookie, v => v.StartsWith("skinora_oid_rv=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReVerifyCallback_NoStateCookie_RedirectsWithReVerifyFailed()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(BuildCallbackUrl());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=re_verify_failed", response.Headers.Location!.ToString());
        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task ReVerifyCallback_SteamIdMismatch_RedirectsWithSteamIdMismatch()
    {
        var userId = await _factory.CreateUserAsync();
        _factory.ValidatorFake.IsValid = true;
        _factory.ValidatorFake.SteamIdToReturn = "76561198999999999";

        var stateCookie = IssueStateCookie(new ReAuthState(
            userId, SteamId, "/profile", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(HttpMethod.Get, BuildCallbackUrl());
        request.Headers.Add("Cookie", $"skinora_oid_rv={stateCookie}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=steam_id_mismatch", response.Headers.Location!.ToString());
        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task ReVerifyCallback_Valid_IssuesSingleUseReAuthTokenAndRedirects()
    {
        var userId = await _factory.CreateUserAsync();
        _factory.ValidatorFake.IsValid = true;
        _factory.ValidatorFake.SteamIdToReturn = SteamId;

        var stateCookie = IssueStateCookie(new ReAuthState(
            userId, SteamId, "/profile", DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(HttpMethod.Get, BuildCallbackUrl());
        request.Headers.Add("Cookie", $"skinora_oid_rv={stateCookie}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());

        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("/profile?reAuthToken=", location);

        var token = Uri.UnescapeDataString(location["/profile?reAuthToken=".Length..]);
        Assert.False(string.IsNullOrWhiteSpace(token));

        using var scope = _factory.Services.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IReAuthTokenValidator>();

        var first = await validator.ValidateAsync(token, default);
        var second = await validator.ValidateAsync(token, default);

        Assert.NotNull(first);
        Assert.Equal(userId, first!.UserId);
        Assert.Equal(SteamId, first.SteamId);
        Assert.Null(second);
    }

    [Fact]
    public async Task CheckAuthenticator_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/check-authenticator",
            new { tradeOfferAccessToken = "abc123xyz" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CheckAuthenticator_Authenticated_ReturnsStubResult()
    {
        var userId = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(userId, SteamId);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/check-authenticator",
            new { tradeOfferAccessToken = "abc123xyz" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.False(data.GetProperty("active").GetBoolean());
        Assert.Equal(
            StubMobileAuthenticatorCheck.DefaultSetupGuideUrl,
            data.GetProperty("setupGuideUrl").GetString());
    }

    private static string BuildCallbackUrl()
    {
        var claimedId = "https://steamcommunity.com/openid/id/" + SteamId;
        var qs = "openid.mode=id_res" +
                 $"&openid.return_to={Uri.EscapeDataString(ReVerifyReturnTo)}" +
                 $"&openid.claimed_id={Uri.EscapeDataString(claimedId)}" +
                 $"&openid.identity={Uri.EscapeDataString(claimedId)}" +
                 "&openid.assoc_handle=1234567890" +
                 "&openid.sig=abcdef";
        return $"/api/v1/auth/steam/re-verify/callback?{qs}";
    }

    private string IssueStateCookie(ReAuthState state)
    {
        using var scope = _factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IReAuthStateProtector>();
        return protector.Protect(state);
    }

    private HttpClient BuildAuthenticatedClient(Guid userId, string steamId)
    {
        var token = IssueAccessToken(userId, steamId);
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string IssueAccessToken(Guid userId, string steamId)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = TestIssuer,
            Audience = TestAudience,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(AuthClaimTypes.UserId, userId.ToString()),
                new Claim(AuthClaimTypes.SteamId, steamId),
                new Claim(AuthClaimTypes.Role, AuthRoles.User),
            }),
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = creds,
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    public sealed class FakeSteamOpenIdValidator : ISteamOpenIdValidator
    {
        public bool IsValid { get; set; } = true;
        public string SteamIdToReturn { get; set; } = SteamId;

        public Task<SteamOpenIdValidationResult> ValidateAsync(
            IReadOnlyDictionary<string, string> callbackParameters,
            CancellationToken cancellationToken)
            => Task.FromResult(IsValid
                ? SteamOpenIdValidationResult.Success(SteamIdToReturn)
                : SteamOpenIdValidationResult.Failure("fake-failure"));

        public Task<SteamOpenIdValidationResult> ValidateAsync(
            IReadOnlyDictionary<string, string> callbackParameters,
            string expectedReturnTo,
            CancellationToken cancellationToken)
            => ValidateAsync(callbackParameters, cancellationToken);
    }

    private sealed class NoopBackgroundJobScheduler : IBackgroundJobScheduler
    {
        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
            => Guid.NewGuid().ToString("N");
        public string Enqueue<T>(Expression<Action<T>> methodCall)
            => Guid.NewGuid().ToString("N");
        public bool Delete(string jobId) => true;
        public void AddOrUpdateRecurring<T>(
            string jobId, Expression<Action<T>> methodCall, string cronExpression)
        { }
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;

        public FakeSteamOpenIdValidator ValidatorFake { get; } = new();

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public async Task<Guid> CreateUserAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = SteamId,
                SteamDisplayName = "Tester",
            };
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        }

        public void Reset()
        {
            ValidatorFake.IsValid = true;
            ValidatorFake.SteamIdToReturn = SteamId;

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<User>().RemoveRange(db.Set<User>());
            db.SaveChanges();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(
                "ConnectionStrings:DefaultConnection",
                "Server=(local);Database=SkinoraTest;Integrated Security=true;TrustServerCertificate=true");
            builder.UseSetting("Hangfire:DashboardEnabled", "false");

            builder.UseSetting("Jwt:Secret", TestSecret);
            builder.UseSetting("Jwt:Issuer", TestIssuer);
            builder.UseSetting("Jwt:Audience", TestAudience);
            builder.UseSetting("Jwt:AccessTokenExpiryMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenExpiryDays", "7");
            builder.UseSetting("Jwt:PreviousSecret", "");

            builder.UseSetting("SteamOpenId:Realm", "https://skinora.test");
            builder.UseSetting("SteamOpenId:ReturnToUrl",
                "https://skinora.test/api/v1/auth/steam/callback");
            builder.UseSetting("SteamOpenId:ReVerifyReturnToUrl", ReVerifyReturnTo);
            builder.UseSetting("SteamOpenId:FrontendCallbackUrl",
                "https://localhost:3000/auth/callback");
            builder.UseSetting("SteamOpenId:DefaultReturnPath", "/dashboard");
            builder.UseSetting("SteamOpenId:WebApiKey", "");

            builder.ConfigureServices(services =>
            {
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

                var hangfireDescriptors = services
                    .Where(d =>
                        (d.ServiceType.Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) ?? false) ||
                        (d.ImplementationType?.Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) ?? false) ||
                        (d.ImplementationFactory?.Method.DeclaringType?.Assembly.GetName().Name?
                            .StartsWith("Hangfire", StringComparison.Ordinal) ?? false))
                    .ToList();
                foreach (var d in hangfireDescriptors) services.Remove(d);

                var startupHookDescriptors = services
                    .Where(d =>
                        d.ImplementationType == typeof(OutboxStartupHook) ||
                        d.ImplementationType == typeof(SettingsBootstrapHook))
                    .ToList();
                foreach (var d in startupHookDescriptors) services.Remove(d);

                services.RemoveAll<IBackgroundJobScheduler>();
                services.AddSingleton<IBackgroundJobScheduler, NoopBackgroundJobScheduler>();

                services.RemoveAll<IDistributedLockProvider>();
                services.AddSingleton<IDistributedLockProvider, InMemoryDistributedLockProvider>();

                var healthCheckDescriptors = services
                    .Where(d => d.ServiceType.FullName?.Contains("HealthCheck",
                        StringComparison.Ordinal) == true)
                    .ToList();
                foreach (var d in healthCheckDescriptors) services.Remove(d);
                services.AddHealthChecks();

                services.RemoveAll<IRateLimiterStore>();
                services.AddSingleton<IRateLimiterStore, InMemoryRateLimiterStore>();

                // T31 — fake Steam OpenID validator + in-memory reAuthToken
                // store so tests do not require a live Redis.
                services.RemoveAll<ISteamOpenIdValidator>();
                services.AddSingleton<ISteamOpenIdValidator>(ValidatorFake);

                services.RemoveAll<IReAuthTokenStore>();
                services.AddSingleton<IReAuthTokenStore>(sp =>
                    new InMemoryReAuthTokenStore(sp.GetRequiredService<TimeProvider>()));
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
            if (disposing) _connection.Dispose();
        }
    }
}
