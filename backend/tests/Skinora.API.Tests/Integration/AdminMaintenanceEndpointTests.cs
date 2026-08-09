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
using Skinora.Admin.Domain.Entities;
using Skinora.API.Outbox;
using Skinora.API.RateLimiting;
using Skinora.API.Startup;
using Skinora.API.Tests.Common;
using Skinora.Auth.Configuration;
using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// WP7 — End-to-end coverage for the admin maintenance/outage control surface
/// (<c>POST /admin/maintenance/freeze|resume</c>, 07 §9.31) plus the generic
/// settings-PUT cache refresh. Exercises the real DI graph through the SQLite +
/// JWT factory: the MANAGE_SETTINGS gate, the four <c>platform.maintenance.*</c>
/// settings, the type→reason timeout-freeze scope, the AuditLog INSERT, the
/// 30s public-cache eviction (observed via <c>GET /platform/maintenance</c>),
/// and the <c>MaintenanceStatusChanged</c> realtime push (captured by a fake
/// publisher).
/// </summary>
public class AdminMaintenanceEndpointTests : IClassFixture<AdminMaintenanceEndpointTests.Factory>
{
    private const string TestSecret = "wp7-maintenance-test-secret-key-min-32-chars-pad!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Factory _factory;

    public AdminMaintenanceEndpointTests(Factory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    // ============================================================
    // Authorization
    // ============================================================

    [Fact]
    public async Task Freeze_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/maintenance/freeze",
            new { type = "PLATFORM_MAINTENANCE" }, JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Freeze_AdminWithoutManageSettings_Returns403()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_FLAGS"]);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/maintenance/freeze",
            new { type = "PLATFORM_MAINTENANCE" }, JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Resume_AdminWithoutManageSettings_Returns403()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["VIEW_FLAGS"]);

        var response = await client.PostAsync("/api/v1/admin/maintenance/resume", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ============================================================
    // Freeze — banner + scope + push + audit
    // ============================================================

    [Fact]
    public async Task Freeze_PlatformMaintenance_SetsBanner_FreezesAllActive_PushesAndAudits()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();

        var created = await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.CREATED);
        var escrowed = await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.SELLER_CONFIRMED);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);
        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/maintenance/freeze",
            new { type = "PLATFORM_MAINTENANCE", message = "Down for upgrade", plannedEnd = "2026-07-01T18:00:00Z" },
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.True(data.GetProperty("active").GetBoolean());
        Assert.Equal("PLATFORM_MAINTENANCE", data.GetProperty("type").GetString());
        // PLATFORM_MAINTENANCE scope = all active → both transactions frozen.
        Assert.Equal(2, data.GetProperty("affectedTransactions").GetInt32());

        // DB: both frozen with the MAINTENANCE reason.
        var createdRow = await _factory.ReadTransactionAsync(created.Id);
        var escrowedRow = await _factory.ReadTransactionAsync(escrowed.Id);
        Assert.NotNull(createdRow.TimeoutFrozenAt);
        Assert.Equal(TimeoutFreezeReason.MAINTENANCE, createdRow.TimeoutFreezeReason);
        Assert.NotNull(escrowedRow.TimeoutFrozenAt);
        Assert.Equal(TimeoutFreezeReason.MAINTENANCE, escrowedRow.TimeoutFreezeReason);

        // Realtime: exactly one banner push reflecting the new state.
        var pushes = _factory.Publisher.MaintenancePushes;
        Assert.Single(pushes);
        Assert.True(pushes[0].Active);
        Assert.Equal("PLATFORM_MAINTENANCE", pushes[0].Type);
        Assert.Equal("Down for upgrade", pushes[0].Message);

        // Audit: one MAINTENANCE_MODE_CHANGED row by the admin.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.Set<Skinora.Platform.Domain.Entities.AuditLog>()
            .AsNoTracking()
            .OrderByDescending(a => a.Id)
            .FirstAsync(a => a.EntityType == "Maintenance");
        Assert.Equal(AuditAction.MAINTENANCE_MODE_CHANGED, audit.Action);
        Assert.Equal(ActorType.ADMIN, audit.ActorType);
        Assert.Equal(admin.Id, audit.ActorId);
        Assert.Contains("PLATFORM_MAINTENANCE", audit.NewValue);

        // 07 §9.31 — the audit envelope carries the four settings plus the
        // affected-transaction count (both transactions were frozen → 2).
        using var auditEnvelope = JsonDocument.Parse(audit.NewValue!);
        Assert.Equal(2, auditEnvelope.RootElement.GetProperty("affectedTransactions").GetInt32());
        Assert.Equal(
            "PLATFORM_MAINTENANCE",
            auditEnvelope.RootElement.GetProperty("settings")
                .GetProperty("platform.maintenance.type").GetString());
    }

    [Fact]
    public async Task Freeze_SteamOutage_FreezesOnlySteamBoundTransactions()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();

        var steamBound = await _factory.CreateTransactionAsync(
            seller.Id, buyer.Id, TransactionStatus.ACCEPTED);
        var paymentStep = await _factory.CreateTransactionAsync(
            seller.Id, buyer.Id, TransactionStatus.SELLER_CONFIRMED);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);
        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/maintenance/freeze",
            new { type = "STEAM_OUTAGE" }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.Equal(1, data.GetProperty("affectedTransactions").GetInt32());

        var steamRow = await _factory.ReadTransactionAsync(steamBound.Id);
        var paymentRow = await _factory.ReadTransactionAsync(paymentStep.Id);
        Assert.Equal(TimeoutFreezeReason.STEAM_OUTAGE, steamRow.TimeoutFreezeReason);
        Assert.Null(paymentRow.TimeoutFrozenAt);
    }

    [Fact]
    public async Task Freeze_BlockchainDegradation_FreezesOnlyPaymentStep()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();

        var created = await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.CREATED);
        var escrowed = await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.SELLER_CONFIRMED);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);
        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/maintenance/freeze",
            new { type = "BLOCKCHAIN_DEGRADATION" }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.Equal(1, data.GetProperty("affectedTransactions").GetInt32());

        var createdRow = await _factory.ReadTransactionAsync(created.Id);
        var escrowedRow = await _factory.ReadTransactionAsync(escrowed.Id);
        Assert.Null(createdRow.TimeoutFrozenAt);
        Assert.Equal(TimeoutFreezeReason.BLOCKCHAIN_DEGRADATION, escrowedRow.TimeoutFreezeReason);
    }

    [Fact]
    public async Task Freeze_PlannedMaintenance_SetsBannerOnly_NoTransactionFrozen()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();

        var created = await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.CREATED);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);
        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/maintenance/freeze",
            new { type = "PLANNED_MAINTENANCE", plannedEnd = "2026-07-01T18:00:00Z" }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        // Banner-only: no freeze (07 §10.2).
        Assert.Equal(0, data.GetProperty("affectedTransactions").GetInt32());

        var createdRow = await _factory.ReadTransactionAsync(created.Id);
        Assert.Null(createdRow.TimeoutFrozenAt);

        // Banner is still active + pushed even though nothing froze.
        var pushes = _factory.Publisher.MaintenancePushes;
        Assert.Single(pushes);
        Assert.True(pushes[0].Active);
        Assert.Equal("PLANNED_MAINTENANCE", pushes[0].Type);
    }

    [Fact]
    public async Task Freeze_InvalidType_Returns400()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/maintenance/freeze",
            new { type = "NONE" }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("VALIDATION_ERROR", json.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Freeze_InvalidPlannedEnd_Returns400()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);

        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/maintenance/freeze",
            new { type = "PLATFORM_MAINTENANCE", plannedEnd = "not-a-date" }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Freeze_MessageExceedsMaxLength_Returns400()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);

        // message > nvarchar(500) column cap must be rejected by the shared
        // SystemSettingsValidator (clean 400) rather than truncated / 500 at
        // SaveChanges. SQLite ignores the column width, so this proves the
        // validator gate, not the DB.
        var response = await client.PostAsJsonAsync(
            "/api/v1/admin/maintenance/freeze",
            new { type = "PLATFORM_MAINTENANCE", message = new string('x', 501) }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("VALIDATION_ERROR", json.GetProperty("error").GetProperty("code").GetString());
    }

    // ============================================================
    // Resume
    // ============================================================

    [Fact]
    public async Task Resume_AfterFreeze_ClearsBanner_ResumesTransactions_Pushes()
    {
        var admin = await _factory.CreateUserAsync();
        var seller = await _factory.CreateUserAsync();
        var buyer = await _factory.CreateUserAsync();
        var escrowed = await _factory.CreateTransactionAsync(seller.Id, buyer.Id, TransactionStatus.SELLER_CONFIRMED);

        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);
        await client.PostAsJsonAsync(
            "/api/v1/admin/maintenance/freeze",
            new { type = "PLATFORM_MAINTENANCE" }, JsonOptions);

        var resume = await client.PostAsync("/api/v1/admin/maintenance/resume", content: null);

        Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
        var data = (await resume.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.False(data.GetProperty("active").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("type").ValueKind);
        Assert.Equal(1, data.GetProperty("affectedTransactions").GetInt32());

        // DB: freeze trio cleared.
        var escrowedRow = await _factory.ReadTransactionAsync(escrowed.Id);
        Assert.Null(escrowedRow.TimeoutFrozenAt);
        Assert.Null(escrowedRow.TimeoutFreezeReason);
        Assert.Null(escrowedRow.TimeoutRemainingSeconds);

        // 07 §9.31 — the resume audit row records the resumed-transaction count.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var resumeAudit = await db.Set<Skinora.Platform.Domain.Entities.AuditLog>()
            .AsNoTracking()
            .OrderByDescending(a => a.Id)
            .FirstAsync(a => a.EntityType == "Maintenance");
        using var resumeEnvelope = JsonDocument.Parse(resumeAudit.NewValue!);
        Assert.Equal(1, resumeEnvelope.RootElement.GetProperty("affectedTransactions").GetInt32());

        // Two pushes total: freeze (active) then resume (inactive).
        var pushes = _factory.Publisher.MaintenancePushes;
        Assert.Equal(2, pushes.Count);
        Assert.False(pushes[^1].Active);
        Assert.Null(pushes[^1].Type);
    }

    [Fact]
    public async Task Resume_WhenNotActive_IsIdempotent_Returns200()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);

        var response = await client.PostAsync("/api/v1/admin/maintenance/resume", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        Assert.False(data.GetProperty("active").GetBoolean());
        Assert.Equal(0, data.GetProperty("affectedTransactions").GetInt32());
    }

    // ============================================================
    // Public read-model consistency (cache evict)
    // ============================================================

    [Fact]
    public async Task PublicMaintenance_ReflectsFreezeState_AfterCacheEvict()
    {
        var admin = await _factory.CreateUserAsync();
        var anon = _factory.CreateClient();

        // Prime the 30s cache with the inactive state.
        var before = await anon.GetFromJsonAsync<JsonElement>("/api/v1/platform/maintenance", JsonOptions);
        Assert.False(before.GetProperty("data").GetProperty("active").GetBoolean());

        var adminClient = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);
        await adminClient.PostAsJsonAsync(
            "/api/v1/admin/maintenance/freeze",
            new { type = "STEAM_OUTAGE", message = "Steam down" }, JsonOptions);

        // The freeze evicted the cache → the public read re-reads the DB.
        var after = await anon.GetFromJsonAsync<JsonElement>("/api/v1/platform/maintenance", JsonOptions);
        var afterData = after.GetProperty("data");
        Assert.True(afterData.GetProperty("active").GetBoolean());
        Assert.Equal("STEAM_OUTAGE", afterData.GetProperty("type").GetString());
        Assert.Equal("Steam down", afterData.GetProperty("message").GetString());
    }

    [Fact]
    public async Task DirectSettingEdit_OfMaintenanceKey_RefreshesPublicStateAndPushes()
    {
        var admin = await _factory.CreateUserAsync();
        var client = BuildClient(admin.Id, admin.SteamId, AuthRoles.Admin, ["MANAGE_SETTINGS"]);

        // Editing a platform.maintenance.* key directly (no dedicated endpoint)
        // must still evict the cache + broadcast so the banner is not stale.
        // (active=true alone is intentionally rejected by the cross-key invariant
        // — the dedicated endpoint exists precisely to set type+active together;
        // here we edit the message field, which is a legal standalone edit.)
        var response = await client.PutAsJsonAsync(
            "/api/v1/admin/settings/platform.maintenance.message",
            new { value = "Scheduled maintenance soon" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pushes = _factory.Publisher.MaintenancePushes;
        Assert.Single(pushes);
        // State stays inactive (only the message changed) but the banner read
        // model was refreshed + re-broadcast.
        Assert.False(pushes[0].Active);
        Assert.Equal("Scheduled maintenance soon", pushes[0].Message);
    }

    // ============================================================
    // Helpers
    // ============================================================

    private HttpClient BuildClient(
        Guid userId, string steamId, string role, IReadOnlyList<string> permissions)
    {
        var token = IssueAccessToken(userId, steamId, role, permissions);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string IssueAccessToken(
        Guid userId, string steamId, string role, IReadOnlyList<string> permissions)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(AuthClaimTypes.UserId, userId.ToString()),
            new(AuthClaimTypes.SteamId, steamId),
            new(AuthClaimTypes.Role, role),
        };
        foreach (var permission in permissions)
            claims.Add(new Claim(AuthClaimTypes.Permission, permission));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = TestIssuer,
            Audience = TestAudience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = creds,
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
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

    /// <summary>
    /// Records maintenance broadcasts so tests can assert the WP7 push without a
    /// live SignalR client. All other publisher methods are no-ops.
    /// </summary>
    public sealed class CapturingRealtimePublisher : INotificationRealtimePublisher
    {
        private readonly object _gate = new();
        private readonly List<NotificationRealtimePayloads.MaintenanceStatusChanged> _maintenance = new();

        public IReadOnlyList<NotificationRealtimePayloads.MaintenanceStatusChanged> MaintenancePushes
        {
            get { lock (_gate) return _maintenance.ToList(); }
        }

        public void Clear()
        {
            lock (_gate) _maintenance.Clear();
        }

        public Task PublishMaintenanceStatusChangedAsync(
            NotificationRealtimePayloads.MaintenanceStatusChanged payload, CancellationToken cancellationToken)
        {
            lock (_gate) _maintenance.Add(payload);
            return Task.CompletedTask;
        }

        public Task PublishNewNotificationAsync(Guid userId, NotificationRealtimePayloads.NewNotification payload, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task PublishUnreadCountChangedAsync(Guid userId, NotificationRealtimePayloads.UnreadCountChanged payload, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task PublishTelegramConnectedAsync(Guid userId, NotificationRealtimePayloads.TelegramConnected payload, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task PublishDiscordConnectedAsync(Guid userId, NotificationRealtimePayloads.DiscordConnected payload, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task PublishAdminBotStatusChangedAsync(NotificationRealtimePayloads.AdminBotStatusChanged payload, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task PublishAdminReconciliationMismatchAsync(NotificationRealtimePayloads.AdminReconciliationMismatch payload, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task PublishAdminHotWalletThresholdBreachedAsync(NotificationRealtimePayloads.AdminHotWalletThresholdBreached payload, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection;
        private int _userSuffix;
        private const string SteamIdPrefix = "76561198777650";

        public CapturingRealtimePublisher Publisher { get; } = new();

        public Factory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
        }

        public async Task<User> CreateUserAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var suffix = Interlocked.Increment(ref _userSuffix);
            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = $"{SteamIdPrefix}{suffix:D3}",
                SteamDisplayName = $"WP7User{suffix:D3}",
                PreferredLanguage = "en",
                CreatedAt = DateTime.UtcNow.AddDays(-30),
            };
            db.Set<User>().Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        public async Task<Transaction> CreateTransactionAsync(
            Guid sellerId, Guid buyerId, TransactionStatus status)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var buyerSteamId = await db.Set<User>()
                .AsNoTracking()
                .Where(u => u.Id == buyerId)
                .Select(u => u.SteamId)
                .FirstAsync();

            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                Status = status,
                SellerId = sellerId,
                BuyerId = buyerId,
                TargetBuyerSteamId = buyerSteamId,
                BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
                BuyerRefundAddress = "TXBuyerRefund000000",
                ItemAssetId = Guid.NewGuid().ToString("N")[..12],
                ItemClassId = "abc-class",
                ItemName = "AK-47 | Redline",
                ItemIconUrl = "https://steamcdn.example/img/test.png",
                StablecoinType = StablecoinType.USDT,
                Price = 100m,
                CommissionRate = 0.02m,
                CommissionAmount = 2m,
                TotalAmount = 102m,
                SellerPayoutAddress = "TXSellerPayout00000",
                PaymentTimeoutMinutes = 1440,
                // Give the payment phase a live deadline so the freeze captures a
                // non-zero remainder for SELLER_CONFIRMED.
                PaymentDeadline = status == TransactionStatus.SELLER_CONFIRMED
                    ? DateTime.UtcNow.AddHours(12)
                    : null,
                SellerConfirmDeadline = status == TransactionStatus.ACCEPTED
                    ? DateTime.UtcNow.AddHours(12)
                    : null,
            };
            db.Set<Transaction>().Add(tx);
            await db.SaveChangesAsync();
            return tx;
        }

        public async Task<Transaction> ReadTransactionAsync(Guid id)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Set<Transaction>().AsNoTracking().FirstAsync(t => t.Id == id);
        }

        public void Reset()
        {
            Publisher.Clear();

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Set<Transaction>().RemoveRange(
                db.Set<Transaction>().IgnoreQueryFilters().ToList());

            db.Database.ExecuteSqlRaw("DELETE FROM AuditLogs");

            // Restore the four maintenance settings to their seeded defaults so
            // each test starts from "no maintenance active".
            db.Database.ExecuteSqlRaw(
                "UPDATE SystemSettings SET Value = 'false', IsConfigured = 1, UpdatedByAdminId = NULL " +
                "WHERE [Key] = 'platform.maintenance.active'");
            db.Database.ExecuteSqlRaw(
                "UPDATE SystemSettings SET Value = 'NONE', IsConfigured = 1, UpdatedByAdminId = NULL " +
                "WHERE [Key] IN ('platform.maintenance.type', 'platform.maintenance.message', 'platform.maintenance.planned_end')");

            db.Set<AdminUserRole>().RemoveRange(
                db.Set<AdminUserRole>().IgnoreQueryFilters().ToList());
            db.Set<AdminRolePermission>().RemoveRange(
                db.Set<AdminRolePermission>().IgnoreQueryFilters().ToList());
            db.Set<AdminRole>().RemoveRange(
                db.Set<AdminRole>().IgnoreQueryFilters().ToList());

            var seedIds = new[] { SeedConstants.SystemUserId };
            db.Set<User>().RemoveRange(
                db.Set<User>().IgnoreQueryFilters().Where(u => !seedIds.Contains(u.Id)));
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

                // WP7 — capture the maintenance broadcast instead of pushing to a
                // real SignalR hub.
                services.RemoveAll<INotificationRealtimePublisher>();
                services.AddSingleton<INotificationRealtimePublisher>(Publisher);

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
    }
}
