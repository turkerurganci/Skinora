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
using Skinora.Auth.Application.ReAuthentication;
using Skinora.Auth.Configuration;
using Skinora.Notifications.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Users.Application.Settings;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

public class AccountSettingsEndpointTests : IClassFixture<AccountSettingsEndpointTests.Factory>
{
    private const string TestSecret = "settings-test-secret-key-minimum-32-chars!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";
    private const string SteamIdPrefix = "76561198555666";
    private const string WebhookSecret = "test-webhook-secret";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public AccountSettingsEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ---------- GET /users/me/settings ----------

    [Fact]
    public async Task GetSettings_NewUser_ReturnsDefaultsPlatformAlwaysOn()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.GetAsync("/api/v1/users/me/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.Equal("en", data.GetProperty("language").GetString());

        var notifications = data.GetProperty("notifications");
        var platform = notifications.GetProperty("platform");
        Assert.True(platform.GetProperty("enabled").GetBoolean());
        Assert.False(platform.GetProperty("canDisable").GetBoolean());

        var email = notifications.GetProperty("email");
        Assert.False(email.GetProperty("enabled").GetBoolean());
        Assert.False(email.GetProperty("verified").GetBoolean());

        var telegram = notifications.GetProperty("telegram");
        Assert.False(telegram.GetProperty("connected").GetBoolean());
    }

    // ---------- PUT /users/me/settings/language ----------

    [Fact]
    public async Task UpdateLanguage_ValidCode_PersistsAndEchoes()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/settings/language", new { language = "tr" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("tr", body.GetProperty("data").GetProperty("language").GetString());

        var persisted = await _factory.GetUserAsync(user.Id);
        Assert.Equal("tr", persisted.PreferredLanguage);
    }

    [Theory]
    [InlineData("de")] // unsupported
    [InlineData("")]
    [InlineData("TRR")]
    public async Task UpdateLanguage_InvalidCode_Returns400(string language)
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/settings/language", new { language });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("INVALID_LANGUAGE",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- PUT /users/me/settings/notifications ----------

    [Fact]
    public async Task UpdateNotifications_TelegramDisabled_NoActiveRow_Returns422ChannelNotConnected()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/settings/notifications",
            new { telegram = new { enabled = false } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("CHANNEL_NOT_CONNECTED",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateNotifications_EmailAddressSet_CreatesPrefRow()
    {
        var user = await _factory.CreateUserAsync(u => u.Email = "user@example.com");
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/settings/notifications",
            new
            {
                email = new { enabled = true, address = "user@example.com" },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var prefs = await _factory.GetPreferencesAsync(user.Id);
        var email = Assert.Single(prefs, p => p.Channel == NotificationChannel.EMAIL);
        Assert.True(email.IsEnabled);
    }

    [Fact]
    public async Task UpdateNotifications_EmailAddressChanged_InvalidatesVerification()
    {
        var now = DateTime.UtcNow;
        var user = await _factory.CreateUserAsync(u =>
        {
            u.Email = "old@example.com";
            u.EmailVerifiedAt = now.AddMinutes(-5);
        });
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/settings/notifications",
            new { email = new { address = "new@example.com" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var persisted = await _factory.GetUserAsync(user.Id);
        Assert.Equal("new@example.com", persisted.Email);
        Assert.Null(persisted.EmailVerifiedAt);
    }

    // ---------- email verify send + verify ----------

    [Fact]
    public async Task SendEmailVerification_NoEmailSet_Returns422()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PostAsync(
            "/api/v1/users/me/settings/email/send-verification", content: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("NO_EMAIL_SET",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SendEmailVerification_SendThenVerify_SetsEmailVerifiedAt()
    {
        var user = await _factory.CreateUserAsync(u => u.Email = "alice@example.com");
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var sendResponse = await client.PostAsync(
            "/api/v1/users/me/settings/email/send-verification", content: null);
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        // Pull the issued code directly from the in-memory store.
        var code = await _factory.PeekEmailCodeAsync(user.Id);
        Assert.NotNull(code);

        var verifyResponse = await client.PostAsJsonAsync(
            "/api/v1/users/me/settings/email/verify", new { code });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var persisted = await _factory.GetUserAsync(user.Id);
        Assert.NotNull(persisted.EmailVerifiedAt);
    }

    [Fact]
    public async Task VerifyEmail_WrongCode_Returns400InvalidCode()
    {
        var user = await _factory.CreateUserAsync(u => u.Email = "bob@example.com");
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        await client.PostAsync(
            "/api/v1/users/me/settings/email/send-verification", content: null);

        var response = await client.PostAsJsonAsync(
            "/api/v1/users/me/settings/email/verify", new { code = "000000" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("INVALID_VERIFICATION_CODE",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task VerifyEmail_NoPendingCode_Returns422Expired()
    {
        var user = await _factory.CreateUserAsync(u => u.Email = "c@example.com");
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PostAsJsonAsync(
            "/api/v1/users/me/settings/email/verify", new { code = "123456" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("VERIFICATION_CODE_EXPIRED",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    // ---------- Telegram connect + webhook + delete ----------

    [Fact]
    public async Task TelegramConnect_ThenWebhook_LinksChannel()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var connectResponse = await client.PostAsync(
            "/api/v1/users/me/settings/telegram/connect", content: null);
        Assert.Equal(HttpStatusCode.OK, connectResponse.StatusCode);
        var body = await connectResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var code = body.GetProperty("data").GetProperty("verificationCode").GetString();
        Assert.StartsWith("SKN-", code, StringComparison.Ordinal);

        var webhookClient = _factory.CreateClient();
        webhookClient.DefaultRequestHeaders.Add("X-Telegram-Bot-Api-Secret-Token", WebhookSecret);

        var webhookResponse = await webhookClient.PostAsJsonAsync(
            "/api/v1/webhooks/telegram",
            new
            {
                message = new
                {
                    text = $"/start {code}",
                    from = new { id = 420042L, username = "playerone" },
                },
            });
        Assert.Equal(HttpStatusCode.OK, webhookResponse.StatusCode);

        var prefs = await _factory.GetPreferencesAsync(user.Id);
        var telegram = Assert.Single(prefs, p => p.Channel == NotificationChannel.TELEGRAM);
        Assert.Equal("420042", telegram.ExternalId);
        Assert.True(telegram.IsEnabled);
    }

    [Fact]
    public async Task TelegramWebhook_MissingSecret_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/webhooks/telegram",
            new { message = new { text = "/start SKN-000000" } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DisconnectTelegram_Idempotent_Ok()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var first = await client.DeleteAsync("/api/v1/users/me/settings/telegram");
        var second = await client.DeleteAsync("/api/v1/users/me/settings/telegram");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    // ---------- Discord connect / callback / disconnect ----------

    [Fact]
    public async Task DiscordConnect_ReturnsAuthorizeUrl()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PostAsync(
            "/api/v1/users/me/settings/discord/connect", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var url = body.GetProperty("data").GetProperty("discordAuthUrl").GetString()!;
        Assert.Contains("client_id=", url, StringComparison.Ordinal);
        Assert.Contains("state=", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscordCallback_ValidState_BindsAccountAndRedirects()
    {
        var user = await _factory.CreateUserAsync();
        var state = await _factory.IssueDiscordStateAsync(user.Id);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(
            $"/api/v1/users/me/settings/discord/callback?code=auth-ok&state={state}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("discord=connected", response.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var prefs = await _factory.GetPreferencesAsync(user.Id);
        Assert.Single(prefs, p => p.Channel == NotificationChannel.DISCORD);
    }

    [Fact]
    public async Task DiscordCallback_UnknownState_RedirectsInvalidState()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(
            "/api/v1/users/me/settings/discord/callback?code=auth-ok&state=not-issued");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("invalid_state", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscordCallback_UserDenied_RedirectsDenied()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(
            "/api/v1/users/me/settings/discord/callback?error=access_denied&state=whatever");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("denied", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    // ---------- Steam trade URL ----------

    [Fact]
    public async Task UpdateTradeUrl_InvalidFormat_Returns422()
    {
        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/settings/steam/trade-url",
            new { tradeUrl = "https://steamcommunity.com/tradeoffer/new/" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("INVALID_TRADE_URL",
            body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UpdateTradeUrl_MaActive_PersistsAndFlagsTrue()
    {
        _factory.TradeHoldStub.Available = true;
        _factory.TradeHoldStub.Active = true;

        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var tradeUrl = "https://steamcommunity.com/tradeoffer/new/?partner=123456&token=abc123xyz";
        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/settings/steam/trade-url", new { tradeUrl });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.True(data.GetProperty("mobileAuthenticatorActive").GetBoolean());

        var persisted = await _factory.GetUserAsync(user.Id);
        Assert.Equal("123456", persisted.SteamTradePartner);
        Assert.Equal("abc123xyz", persisted.SteamTradeAccessToken);
        Assert.True(persisted.MobileAuthenticatorVerified);
    }

    [Fact]
    public async Task UpdateTradeUrl_MaInactive_PersistsAndReturnsSetupGuide()
    {
        _factory.TradeHoldStub.Available = true;
        _factory.TradeHoldStub.Active = false;

        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var tradeUrl = "https://steamcommunity.com/tradeoffer/new/?partner=789012&token=xyzxyzxy";
        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/settings/steam/trade-url", new { tradeUrl });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        Assert.False(data.GetProperty("mobileAuthenticatorActive").GetBoolean());
        Assert.False(string.IsNullOrEmpty(data.GetProperty("setupGuideUrl").GetString()));

        var persisted = await _factory.GetUserAsync(user.Id);
        Assert.False(persisted.MobileAuthenticatorVerified);
    }

    [Fact]
    public async Task UpdateTradeUrl_SteamApiUnavailable_Returns503ButPersists()
    {
        _factory.TradeHoldStub.Available = false;

        var user = await _factory.CreateUserAsync();
        var client = BuildAuthenticatedClient(user.Id, user.SteamId);

        var tradeUrl = "https://steamcommunity.com/tradeoffer/new/?partner=111111&token=downtokn";
        var response = await client.PutAsJsonAsync(
            "/api/v1/users/me/settings/steam/trade-url", new { tradeUrl });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var persisted = await _factory.GetUserAsync(user.Id);
        Assert.Equal("111111", persisted.SteamTradePartner);
        Assert.False(persisted.MobileAuthenticatorVerified);
    }

    // ---------- helpers ----------

    private HttpClient BuildAuthenticatedClient(Guid userId, string steamId)
    {
        var token = IssueAccessToken(userId, steamId);
        var client = _factory.CreateClient();
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

    // ---------- test doubles ----------

    public sealed class ConfigurableTradeHoldStub : ITradeHoldChecker
    {
        public bool Available { get; set; } = true;
        public bool Active { get; set; } = true;
        public string? SetupGuideUrl { get; set; } = StubTradeHoldChecker.DefaultSetupGuideUrl;

        public Task<TradeHoldResult> CheckAsync(
            string steamId64, string tradeOfferAccessToken, CancellationToken cancellationToken)
            => Task.FromResult(new TradeHoldResult(Available, Active, Active ? null : SetupGuideUrl));
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

    // ---------- factory ----------

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;
        private int _userSuffix;

        public ConfigurableTradeHoldStub TradeHoldStub { get; } = new();

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public async Task<User> CreateUserAsync(Action<User>? customize = null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Interlocked.Increment(ref _userSuffix);
            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = $"{SteamIdPrefix}{suffix:D3}",
                SteamDisplayName = "SettingsTester",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
            };
            customize?.Invoke(user);

            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public async Task<User> GetUserAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<User>().AsNoTracking().FirstAsync(u => u.Id == userId);
        }

        public async Task<List<UserNotificationPreference>> GetPreferencesAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<UserNotificationPreference>()
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .ToListAsync();
        }

        public async Task<string?> PeekEmailCodeAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IEmailVerificationCodeStore>();
            var entry = await store.PeekAsync(userId, CancellationToken.None);
            return entry?.Code;
        }

        public async Task<string> IssueDiscordStateAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IDiscordOAuthStateStore>();
            var state = Guid.NewGuid().ToString("N");
            await store.IssueAsync(state, userId, TimeSpan.FromMinutes(5), CancellationToken.None);
            return state;
        }

        public void Reset()
        {
            TradeHoldStub.Available = true;
            TradeHoldStub.Active = true;

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var prefs = db.Set<UserNotificationPreference>()
                .IgnoreQueryFilters()
                .ToList();
            db.Set<UserNotificationPreference>().RemoveRange(prefs);
            var seedIds = new[] { Skinora.Shared.Domain.Seed.SeedConstants.SystemUserId };
            db.Set<User>().RemoveRange(db.Set<User>().Where(u => !seedIds.Contains(u.Id)));
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
            builder.UseSetting("SteamOpenId:FrontendCallbackUrl",
                "https://localhost:3000/auth/callback");
            builder.UseSetting("SteamOpenId:DefaultReturnPath", "/dashboard");
            builder.UseSetting("SteamOpenId:WebApiKey", "");

            builder.UseSetting("Telegram:BotUrl", "https://t.me/SkinoraBot");
            builder.UseSetting("Telegram:WebhookSecretToken", WebhookSecret);
            builder.UseSetting("Telegram:CodeTtlSeconds", "300");

            builder.UseSetting("Discord:ClientId", "discord-test-client");
            builder.UseSetting("Discord:ClientSecret", "discord-test-secret");
            builder.UseSetting("Discord:RedirectUri",
                "https://skinora.test/api/v1/users/me/settings/discord/callback");
            builder.UseSetting("Discord:Scope", "identify");
            builder.UseSetting("Discord:StateTtlSeconds", "600");
            builder.UseSetting("Discord:SuccessRedirectUrl", "/settings?discord=connected");
            builder.UseSetting("Discord:FailureRedirectUrl", "/settings?discord=error");

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

                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

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

                services.RemoveAll<IReAuthTokenStore>();
                services.AddSingleton<IReAuthTokenStore>(sp =>
                    new InMemoryReAuthTokenStore(sp.GetRequiredService<TimeProvider>()));

                // T35 — in-memory store swap for the three Redis-backed stores.
                services.RemoveAll<IEmailVerificationCodeStore>();
                services.AddSingleton<IEmailVerificationCodeStore>(sp =>
                    new InMemoryEmailVerificationCodeStore(sp.GetRequiredService<TimeProvider>()));

                services.RemoveAll<ITelegramVerificationStore>();
                services.AddSingleton<ITelegramVerificationStore>(sp =>
                    new InMemoryTelegramVerificationStore(sp.GetRequiredService<TimeProvider>()));

                services.RemoveAll<IDiscordOAuthStateStore>();
                services.AddSingleton<IDiscordOAuthStateStore>(sp =>
                    new InMemoryDiscordOAuthStateStore(sp.GetRequiredService<TimeProvider>()));

                services.RemoveAll<ITradeHoldChecker>();
                services.AddSingleton<ITradeHoldChecker>(TradeHoldStub);
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
