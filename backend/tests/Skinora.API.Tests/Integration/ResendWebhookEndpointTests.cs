using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Startup;
using Skinora.API.Tests.Common;
using Skinora.Notifications.Domain.Entities;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;
using Skinora.Shared.Persistence.Webhooks;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T78 — integration coverage for the Resend webhook surface
/// (<c>POST /api/v1/webhooks/resend</c>). Exercises
/// <see cref="API.Middleware.ResendWebhookSignatureMiddleware"/> (Svix
/// signature, replay window, svix-id idempotency via ProcessedNonces)
/// and end-to-end side effects (EMAIL channel disable on bounce /
/// complaint / suppression).
/// </summary>
public sealed class ResendWebhookEndpointTests : IClassFixture<ResendWebhookEndpointTests.Factory>
{
    private const string ResendSigningSecret = "whsec_dGVzdC1zZWNyZXQtMzItYnl0ZS1zZWNyZXQta2V5XzEyMzQ=";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly Factory _factory;

    public ResendWebhookEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    [Fact]
    public async Task Resend_MissingHeaders_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/webhooks/resend",
            new { type = "email.bounced", data = new { to = new[] { "x@y.com" } } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Resend_InvalidSignature_Returns401()
    {
        var client = _factory.CreateClient();
        var body = """{"type":"email.bounced","data":{"to":["user@example.com"]}}""";

        using var request = BuildRequest(body, "msg_test", UnixNow(), "v1,deadbeef");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Resend_StaleTimestamp_Returns401()
    {
        var client = _factory.CreateClient();
        var body = """{"type":"email.bounced","data":{"to":["user@example.com"]}}""";
        var staleTs = (DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds()).ToString();
        var sig = BuildSignature("msg_test", staleTs, body);

        using var request = BuildRequest(body, "msg_test", staleTs, sig);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Resend_Bounced_DisablesEmailPreference()
    {
        var userId = await _factory.SeedUserWithEnabledEmailPreferenceAsync("bouncer@example.com");
        var client = _factory.CreateClient();

        var response = await SendSignedAsync(client, BouncedEnvelope("bouncer@example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertResult(response, "Applied");

        var enabled = await _factory.IsEmailPreferenceEnabledAsync(userId);
        Assert.False(enabled);
    }

    [Fact]
    public async Task Resend_Suppressed_DisablesEmailPreference()
    {
        var userId = await _factory.SeedUserWithEnabledEmailPreferenceAsync("suppressed@example.com");
        var client = _factory.CreateClient();

        var response = await SendSignedAsync(client, SuppressedEnvelope("suppressed@example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertResult(response, "Applied");

        var enabled = await _factory.IsEmailPreferenceEnabledAsync(userId);
        Assert.False(enabled);
    }

    [Fact]
    public async Task Resend_DuplicateSvixId_ReturnsIdempotentOnSecondCall()
    {
        await _factory.SeedUserWithEnabledEmailPreferenceAsync("dup@example.com");
        var client = _factory.CreateClient();

        var envelope = BouncedEnvelope("dup@example.com");
        var first = await SendSignedAsync(client, envelope, svixId: "msg_dup_42");
        var second = await SendSignedAsync(client, envelope, svixId: "msg_dup_42");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await AssertResult(first, "Applied");
        await AssertResult(second, "Idempotent");
    }

    [Fact]
    public async Task Resend_UnknownRecipient_ReturnsUnknownRecipient()
    {
        var client = _factory.CreateClient();
        var response = await SendSignedAsync(client, BouncedEnvelope("nobody@example.com"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertResult(response, "UnknownRecipient");
    }

    [Fact]
    public async Task Resend_DeliveryDelayed_AcknowledgedNoStateChange()
    {
        var userId = await _factory.SeedUserWithEnabledEmailPreferenceAsync("delayed@example.com");
        var client = _factory.CreateClient();

        var envelope = new
        {
            type = "email.delivery_delayed",
            data = new
            {
                email_id = Guid.NewGuid().ToString(),
                to = new[] { "delayed@example.com" },
            },
        };

        var response = await SendSignedAsync(client, envelope);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertResult(response, "Acknowledged");

        // EMAIL preference stays ENABLED — delayed events do not disable.
        var enabled = await _factory.IsEmailPreferenceEnabledAsync(userId);
        Assert.True(enabled);
    }

    [Fact]
    public async Task Resend_UnknownEventType_Acknowledged()
    {
        var client = _factory.CreateClient();
        var envelope = new
        {
            type = "contact.created",
            data = new { email_id = "irrelevant", to = new[] { "x@y.com" } },
        };

        var response = await SendSignedAsync(client, envelope);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertResult(response, "Acknowledged");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static object BouncedEnvelope(string recipient) => new
    {
        type = "email.bounced",
        data = new
        {
            email_id = Guid.NewGuid().ToString(),
            to = new[] { recipient },
        },
    };

    private static object SuppressedEnvelope(string recipient) => new
    {
        type = "email.suppressed",
        data = new
        {
            email_id = Guid.NewGuid().ToString(),
            to = new[] { recipient },
        },
    };

    private static async Task AssertResult(HttpResponseMessage response, string expected)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var data = json.GetProperty("data");
        Assert.Equal(expected, data.GetProperty("result").GetString());
    }

    private static async Task<HttpResponseMessage> SendSignedAsync(
        HttpClient client,
        object envelope,
        string? svixId = null)
    {
        var body = JsonSerializer.Serialize(envelope, JsonOptions);
        var unix = UnixNow();
        var id = svixId ?? "msg_" + Guid.NewGuid().ToString("N");
        var signature = BuildSignature(id, unix, body);

        using var request = BuildRequest(body, id, unix, signature);
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage BuildRequest(string body, string svixId, string svixTimestamp, string svixSignature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/resend")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("svix-id", svixId);
        request.Headers.Add("svix-timestamp", svixTimestamp);
        request.Headers.Add("svix-signature", svixSignature);
        return request;
    }

    private static string UnixNow()
        => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

    private static string BuildSignature(string msgId, string unix, string body)
    {
        var key = Convert.FromBase64String(ResendSigningSecret["whsec_".Length..]);
        var signed = $"{msgId}.{unix}.{body}";
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed));
        return "v1," + Convert.ToBase64String(hash);
    }

    // ── Test fixture ─────────────────────────────────────────────────

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");

        public Factory()
        {
            _connection.Open();
        }

        public async Task<Guid> SeedUserWithEnabledEmailPreferenceAsync(string email)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = "76561198000999" + Random.Shared.Next(100, 999),
                SteamDisplayName = "ResendWebhookUser",
                Email = email,
                EmailVerifiedAt = DateTime.UtcNow,
                PreferredLanguage = "en",
            };
            db.Set<User>().Add(user);

            db.Set<UserNotificationPreference>().Add(new UserNotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Channel = NotificationChannel.EMAIL,
                IsEnabled = true,
                ExternalId = email,
                VerifiedAt = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
            return user.Id;
        }

        public async Task<bool> IsEmailPreferenceEnabledAsync(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pref = await db.Set<UserNotificationPreference>()
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Channel == NotificationChannel.EMAIL);
            return pref?.IsEnabled ?? false;
        }

        public void Reset()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<ProcessedNonce>().ExecuteDelete();
            db.Set<OutboxMessage>().ExecuteDelete();
            db.Set<UserNotificationPreference>().IgnoreQueryFilters().ExecuteDelete();
            db.Set<User>().IgnoreQueryFilters()
                .Where(u => u.Id != Skinora.Shared.Domain.Seed.SeedConstants.SystemUserId)
                .ExecuteDelete();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Server=(local);Database=SkinoraTest;Integrated Security=true;TrustServerCertificate=true");
            builder.UseSetting("Hangfire:DashboardEnabled", "false");

            builder.UseSetting("Jwt:Secret", "test-resend-webhook-jwt-secret-32!!");
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

            builder.UseSetting("SteamSidecar:BaseUrl", "http://localhost:65500");
            builder.UseSetting("SteamSidecar:InternalKey", "test-internal-key");

            builder.UseSetting("BlockchainSidecar:BaseUrl", "http://localhost:65501");
            builder.UseSetting("BlockchainSidecar:InternalKey", "test-internal-key");

            builder.UseSetting("Webhook:SteamSharedSecret", "skinora-test-steam-32!!!!!!!!!!!!!!!");
            builder.UseSetting("Webhook:BlockchainSharedSecret", "skinora-test-blockchain-32!!!!!!!!");
            builder.UseSetting("Webhook:ReplayWindowSeconds", "300");
            builder.UseSetting("Webhook:NonceRetentionSeconds", "3600");

            // T78 — Resend webhook signing secret. Provider stays at "logging"
            // so the endpoint does NOT require a Resend HttpClient registration
            // (the webhook side stays callable in stub mode).
            builder.UseSetting("Resend:Provider", "logging");
            builder.UseSetting("Resend:WebhookSigningSecret", ResendSigningSecret);
            builder.UseSetting("Resend:WebhookReplayWindowSeconds", "300");

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
    }
}
