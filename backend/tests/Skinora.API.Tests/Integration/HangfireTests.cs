using System;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Skinora.API.BackgroundJobs;
using Skinora.API.Tests.Common;
using Skinora.Auth.Configuration;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.API.Tests.Integration;

/// <summary>
/// T09 — Hangfire integration tests. Validates the IBackgroundJobScheduler
/// abstraction, the schedule/delete behaviour required for the timeout
/// scheduling pattern (09 §13.3), the freeze/resume pattern (09 §13.6), the
/// state validation pattern from 09 §13.3, the global AutomaticRetry(3) filter
/// (09 §13.5), and the admin-only dashboard auth (T09 kabul kriteri).
/// </summary>
public class HangfireTests : IClassFixture<HangfireTests.TestFactory>
{
    private const string TestSecret = "test-jwt-secret-key-minimum-32-characters-long!!";
    private const string TestIssuer = "skinora";
    private const string TestAudience = "skinora-client";

    private readonly TestFactory _factory;
    private readonly HttpClient _client;

    public HangfireTests(TestFactory factory)
    {
        _factory = factory;
        // Hangfire dashboard internally calls IAntiforgery.GetAndStoreTokens on
        // every request to populate CSRF tokens in its built-in form submits
        // (job retry/delete buttons). Skinora's Antiforgery configuration uses
        // CookieSecurePolicy.Always (Program.cs), so the antiforgery system
        // throws on plain HTTP requests. Production traffic always reaches the
        // backend over HTTPS via Nginx, so we use an https:// BaseAddress here
        // to mirror that scheme.
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
    }

    #region IBackgroundJobScheduler — DI registration

    [Fact]
    public void IBackgroundJobScheduler_IsRegistered_InDi()
    {
        using var scope = _factory.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundJobScheduler>();

        Assert.NotNull(scheduler);
        Assert.IsType<HangfireBackgroundJobScheduler>(scheduler);
    }

    #endregion

    #region Schedule / Delete — base scheduler behaviour

    [Fact]
    public void Schedule_DelayedJob_ReturnsNonEmptyJobId()
    {
        using var scope = _factory.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundJobScheduler>();

        var jobId = scheduler.Schedule<ITestJobTarget>(
            t => t.Run(Guid.NewGuid()),
            TimeSpan.FromMinutes(30));

        Assert.False(string.IsNullOrWhiteSpace(jobId));
    }

    [Fact]
    public void Schedule_ThenDelete_ReturnsTrue_AndStorageMarksDeleted()
    {
        using var scope = _factory.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundJobScheduler>();
        var storage = scope.ServiceProvider.GetRequiredService<JobStorage>();

        var jobId = scheduler.Schedule<ITestJobTarget>(
            t => t.Run(Guid.NewGuid()),
            TimeSpan.FromMinutes(30));

        var deleted = scheduler.Delete(jobId);
        Assert.True(deleted);

        // The job should now be in the Deleted state in Hangfire's storage.
        using var connection = storage.GetConnection();
        var stateData = connection.GetStateData(jobId);
        Assert.NotNull(stateData);
        Assert.Equal(DeletedState.StateName, stateData.Name);
    }

    [Fact]
    public void Delete_NonExistentJob_ReturnsFalse()
    {
        using var scope = _factory.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundJobScheduler>();

        var deleted = scheduler.Delete("non-existent-job-id");
        Assert.False(deleted);
    }

    [Fact]
    public void Enqueue_ReturnsNonEmptyJobId()
    {
        using var scope = _factory.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundJobScheduler>();

        var jobId = scheduler.Enqueue<ITestJobTarget>(
            t => t.Run(Guid.NewGuid()));

        Assert.False(string.IsNullOrWhiteSpace(jobId));
    }

    #endregion

    #region Freeze / Resume pattern (09 §13.6)

    [Fact]
    public void FreezeResume_Pattern_DeletesOldJob_AndReschedulesNewOne()
    {
        // 09 §13.6 — freeze deletes the active timeout job; resume reschedules
        // a new job against the extended deadline. Each step uses the public
        // IBackgroundJobScheduler API; this test proves the sequence works
        // end-to-end against Hangfire storage.
        using var scope = _factory.Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundJobScheduler>();
        var storage = scope.ServiceProvider.GetRequiredService<JobStorage>();
        var transactionId = Guid.NewGuid();

        // 1. Initial schedule (active timeout window)
        var originalJobId = scheduler.Schedule<ITestJobTarget>(
            t => t.Run(transactionId),
            TimeSpan.FromMinutes(30));

        // 2. Freeze — admin triggers, scheduler.Delete cancels the pending job
        var deleted = scheduler.Delete(originalJobId);
        Assert.True(deleted);

        // 3. Resume — new schedule is created against the extended deadline.
        //    The new jobId must differ from the original.
        var resumedJobId = scheduler.Schedule<ITestJobTarget>(
            t => t.Run(transactionId),
            TimeSpan.FromMinutes(45)); // original 30 + 15 frozen duration

        Assert.False(string.IsNullOrWhiteSpace(resumedJobId));
        Assert.NotEqual(originalJobId, resumedJobId);

        // The original job is in Deleted state, the new one in Scheduled.
        using var connection = storage.GetConnection();
        var originalState = connection.GetStateData(originalJobId);
        var resumedState = connection.GetStateData(resumedJobId);
        Assert.Equal(DeletedState.StateName, originalState.Name);
        Assert.Equal(ScheduledState.StateName, resumedState.Name);
    }

    #endregion

    #region State validation pattern (09 §13.3)

    // The state validation pattern is a runtime defensive check executed by
    // every timeout/warning job handler. The handler reloads the entity, and
    // no-ops if any of the gating conditions no longer hold (status changed,
    // frozen, deadline shifted, etc.). The sample handler below demonstrates
    // the canonical shape required of T44+ timeout handlers.

    [Fact]
    public void StateValidatedJob_Processes_WhenAllConditionsHold()
    {
        var state = new SampleEntityStore();
        var id = Guid.NewGuid();
        state.Items[id] = new SampleEntity(
            Status: "ACTIVE",
            FrozenAt: null,
            Deadline: DateTime.UtcNow.AddMinutes(-1)); // expired → should process

        var handler = new SampleStateValidatedTimeoutHandler(state);
        handler.Run(id);

        Assert.True(state.Items[id].Processed);
    }

    [Fact]
    public void StateValidatedJob_NoOps_WhenEntityMissing()
    {
        var state = new SampleEntityStore();
        var handler = new SampleStateValidatedTimeoutHandler(state);

        // Should not throw, should not mutate state
        handler.Run(Guid.NewGuid());
        Assert.Empty(state.Items);
    }

    [Fact]
    public void StateValidatedJob_NoOps_WhenStatusChanged()
    {
        var state = new SampleEntityStore();
        var id = Guid.NewGuid();
        state.Items[id] = new SampleEntity(
            Status: "CANCELLED",
            FrozenAt: null,
            Deadline: DateTime.UtcNow.AddMinutes(-1));

        new SampleStateValidatedTimeoutHandler(state).Run(id);

        Assert.False(state.Items[id].Processed);
    }

    [Fact]
    public void StateValidatedJob_NoOps_WhenFrozen()
    {
        var state = new SampleEntityStore();
        var id = Guid.NewGuid();
        state.Items[id] = new SampleEntity(
            Status: "ACTIVE",
            FrozenAt: DateTime.UtcNow.AddMinutes(-2), // freeze active
            Deadline: DateTime.UtcNow.AddMinutes(-1));

        new SampleStateValidatedTimeoutHandler(state).Run(id);

        Assert.False(state.Items[id].Processed);
    }

    [Fact]
    public void StateValidatedJob_NoOps_WhenDeadlineNotReached()
    {
        var state = new SampleEntityStore();
        var id = Guid.NewGuid();
        state.Items[id] = new SampleEntity(
            Status: "ACTIVE",
            FrozenAt: null,
            Deadline: DateTime.UtcNow.AddMinutes(+30)); // not yet expired

        new SampleStateValidatedTimeoutHandler(state).Run(id);

        Assert.False(state.Items[id].Processed);
    }

    #endregion

    #region Global AutomaticRetry(3) filter (09 §13.5)

    [Fact]
    public void GlobalJobFilters_ContainAutomaticRetry_WithThreeAttempts()
    {
        // Force WebApplicationFactory to build the service provider so the
        // Hangfire configuration delegate has been executed and global filters
        // have been applied.
        _ = _factory.Services;

        var retryFilter = GlobalJobFilters.Filters
            .Select(f => f.Instance)
            .OfType<AutomaticRetryAttribute>()
            .FirstOrDefault();

        Assert.NotNull(retryFilter);
        Assert.Equal(3, retryFilter!.Attempts);
    }

    #endregion

    #region Dashboard authorization

    [Fact]
    public async Task HangfireDashboard_AnonymousRequest_Returns401()
    {
        var response = await _client.GetAsync("/hangfire");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HangfireDashboard_AuthenticatedNonAdmin_Returns403()
    {
        // Hangfire returns 401 only for unauthenticated requests; an
        // authenticated user without the required claims gets 403 Forbidden,
        // matching standard HTTP semantics (RFC 7235 §3.1).
        var token = GenerateToken(role: AuthRoles.User);
        var request = new HttpRequestMessage(HttpMethod.Get, "/hangfire");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HangfireDashboard_AdminToken_Returns200()
    {
        var token = GenerateToken(role: AuthRoles.Admin);
        var request = new HttpRequestMessage(HttpMethod.Get, "/hangfire");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HangfireDashboard_SuperAdminToken_Returns200()
    {
        var token = GenerateToken(role: AuthRoles.SuperAdmin);
        var request = new HttpRequestMessage(HttpMethod.Get, "/hangfire");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Test plumbing

    private static string GenerateToken(string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: new[]
            {
                new Claim(AuthClaimTypes.UserId, "test-user-id"),
                new Claim(AuthClaimTypes.Role, role),
            },
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Public test job target so Hangfire can serialize the
    /// <c>Schedule&lt;T&gt;</c> expression and resolve <c>T</c> from DI when
    /// the worker eventually runs the job. The body never executes inside
    /// these tests (no Hangfire server is started against InMemoryStorage),
    /// so the implementation is intentionally trivial.
    /// </summary>
    public interface ITestJobTarget
    {
        void Run(Guid id);
    }

    public sealed class TestJobTarget : ITestJobTarget
    {
        public void Run(Guid id) { /* no-op for tests */ }
    }

    /// <summary>
    /// Sample handler demonstrating the 09 §13.3 state validation pattern.
    /// Real timeout job handlers (T47–T50) follow the same shape against
    /// Transaction entities.
    /// </summary>
    private sealed class SampleStateValidatedTimeoutHandler
    {
        private readonly SampleEntityStore _store;

        public SampleStateValidatedTimeoutHandler(SampleEntityStore store)
        {
            _store = store;
        }

        public void Run(Guid id)
        {
            // 09 §13.3 — defensive checks; no-op when any condition fails.
            if (!_store.Items.TryGetValue(id, out var entity)) return;
            if (entity.Status != "ACTIVE") return;
            if (entity.FrozenAt != null) return;
            if (entity.Deadline > DateTime.UtcNow) return;

            // All gating conditions passed → perform the timeout action.
            _store.Items[id] = entity with { Processed = true };
        }
    }

    private sealed class SampleEntityStore
    {
        public ConcurrentDictionary<Guid, SampleEntity> Items { get; } = new();
    }

    private sealed record SampleEntity(
        string Status,
        DateTime? FrozenAt,
        DateTime Deadline,
        bool Processed = false);

    /// <summary>
    /// Hangfire-specific test host. Inherits the InMemoryStorage swap from
    /// <see cref="HangfireBypassFactory"/> and additionally registers the
    /// test job target so Hangfire can resolve <see cref="ITestJobTarget"/>
    /// when (de)serializing scheduled job expressions, plus the JWT settings
    /// consumed by <c>GenerateToken</c> in the dashboard auth tests.
    /// </summary>
    public sealed class TestFactory : HangfireBypassFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Apply the base Hangfire bypass first
            base.ConfigureWebHost(builder);

            // JWT settings consumed by GenerateToken in the dashboard tests
            builder.UseSetting("Jwt:Secret", TestSecret);
            builder.UseSetting("Jwt:Issuer", TestIssuer);
            builder.UseSetting("Jwt:Audience", TestAudience);
            builder.UseSetting("Jwt:AccessTokenExpiryMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenExpiryDays", "7");

            builder.ConfigureServices(services =>
            {
                // Register the test job target so Hangfire can resolve
                // ITestJobTarget when (de)serializing scheduled job expressions.
                services.AddScoped<ITestJobTarget, TestJobTarget>();
            });
        }
    }

    #endregion
}
