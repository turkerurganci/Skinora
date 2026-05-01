using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Platform.Tests.Integration;

/// <summary>
/// EF-backed coverage for <see cref="AuditLogger"/> (T42, 09 §18.6).
/// Verifies the actor invariant (06 §8.6a), CreatedAt stamping, and that
/// staged rows persist via the caller's own SaveChangesAsync.
/// </summary>
public class AuditLoggerTests : IntegrationTestBase
{
    static AuditLoggerTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private static readonly DateTime FrozenNow =
        new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

    private User _admin = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        // SeedConstants.SystemUserId is already inserted by UserConfiguration
        // HasData (06 §8.9) when EnsureCreatedAsync builds the schema, so we
        // only seed the test-specific admin row here.
        _admin = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198555000600",
            SteamDisplayName = "AuditAdmin",
        };
        context.Set<User>().Add(_admin);
        await context.SaveChangesAsync();
    }

    private AuditLogger CreateLogger() =>
        new(Context, new FixedClock(FrozenNow));

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LogAsync_Admin_Actor_Stages_Row_With_Frozen_CreatedAt()
    {
        var logger = CreateLogger();

        await logger.LogAsync(new AuditLogEntry(
            UserId: _admin.Id,
            ActorId: _admin.Id,
            ActorType: ActorType.ADMIN,
            Action: AuditAction.SYSTEM_SETTING_CHANGED,
            EntityType: "SystemSetting",
            EntityId: "commission_rate",
            OldValue: "{\"value\":\"0.02\"}",
            NewValue: "{\"value\":\"0.03\"}",
            IpAddress: "10.0.0.5"), CancellationToken.None);

        // Logger only stages — caller commits.
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var row = await readCtx.Set<AuditLog>()
            .OrderByDescending(a => a.Id)
            .FirstAsync(a => a.EntityId == "commission_rate");

        Assert.Equal(ActorType.ADMIN, row.ActorType);
        Assert.Equal(AuditAction.SYSTEM_SETTING_CHANGED, row.Action);
        Assert.Equal(_admin.Id, row.ActorId);
        Assert.Equal(FrozenNow, row.CreatedAt);
        Assert.Equal("10.0.0.5", row.IpAddress);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LogAsync_System_Actor_Allows_SystemUserId_And_Null_Subject()
    {
        var logger = CreateLogger();

        await logger.LogAsync(new AuditLogEntry(
            UserId: null,
            ActorId: SeedConstants.SystemUserId,
            ActorType: ActorType.SYSTEM,
            Action: AuditAction.WALLET_REFUND,
            EntityType: "Wallet",
            EntityId: Guid.NewGuid().ToString(),
            OldValue: null,
            NewValue: "{\"amount\":\"5.00\"}",
            IpAddress: null), CancellationToken.None);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var row = await readCtx.Set<AuditLog>()
            .OrderByDescending(a => a.Id)
            .FirstAsync(a => a.ActorType == ActorType.SYSTEM);
        Assert.Null(row.UserId);
        Assert.Equal(SeedConstants.SystemUserId, row.ActorId);
    }

    [Fact]
    public async Task LogAsync_System_Actor_With_NonSystem_Guid_Throws()
    {
        var logger = CreateLogger();

        var ex = await Assert.ThrowsAsync<InvalidActorException>(() =>
            logger.LogAsync(new AuditLogEntry(
                UserId: null,
                ActorId: _admin.Id,
                ActorType: ActorType.SYSTEM,
                Action: AuditAction.WALLET_REFUND,
                EntityType: "Wallet",
                EntityId: "abc",
                OldValue: null,
                NewValue: null,
                IpAddress: null), CancellationToken.None));
        Assert.Contains("ActorType.SYSTEM", ex.Message);
    }

    [Fact]
    public async Task LogAsync_Admin_Actor_With_Empty_Guid_Throws()
    {
        var logger = CreateLogger();

        var ex = await Assert.ThrowsAsync<InvalidActorException>(() =>
            logger.LogAsync(new AuditLogEntry(
                UserId: null,
                ActorId: Guid.Empty,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.USER_BANNED,
                EntityType: "User",
                EntityId: Guid.NewGuid().ToString(),
                OldValue: null,
                NewValue: null,
                IpAddress: null), CancellationToken.None));
        Assert.Contains("non-empty ActorId", ex.Message);
    }

    [Fact]
    public async Task LogAsync_User_Actor_With_System_Guid_Throws()
    {
        var logger = CreateLogger();

        var ex = await Assert.ThrowsAsync<InvalidActorException>(() =>
            logger.LogAsync(new AuditLogEntry(
                UserId: SeedConstants.SystemUserId,
                ActorId: SeedConstants.SystemUserId,
                ActorType: ActorType.USER,
                Action: AuditAction.WALLET_ADDRESS_CHANGED,
                EntityType: "User",
                EntityId: SeedConstants.SystemUserId.ToString(),
                OldValue: null,
                NewValue: null,
                IpAddress: null), CancellationToken.None));
        Assert.Contains("reserved for ActorType.SYSTEM", ex.Message);
    }

    [Theory]
    [InlineData("", "EntityType")]
    [InlineData(null, "EntityType")]
    public async Task LogAsync_Empty_EntityType_Throws(string? entityType, string expectedField)
    {
        var logger = CreateLogger();

        var ex = await Assert.ThrowsAsync<InvalidActorException>(() =>
            logger.LogAsync(new AuditLogEntry(
                UserId: _admin.Id,
                ActorId: _admin.Id,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.USER_BANNED,
                EntityType: entityType!,
                EntityId: "abc",
                OldValue: null,
                NewValue: null,
                IpAddress: null), CancellationToken.None));
        Assert.Contains(expectedField, ex.Message);
    }

    [Fact]
    public async Task LogAsync_Empty_EntityId_Throws()
    {
        var logger = CreateLogger();

        var ex = await Assert.ThrowsAsync<InvalidActorException>(() =>
            logger.LogAsync(new AuditLogEntry(
                UserId: _admin.Id,
                ActorId: _admin.Id,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.USER_BANNED,
                EntityType: "User",
                EntityId: "",
                OldValue: null,
                NewValue: null,
                IpAddress: null), CancellationToken.None));
        Assert.Contains("EntityId", ex.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LogAsync_Persisted_Row_Cannot_Be_Updated_Or_Deleted()
    {
        // 06 §3.20 + AppDbContext.EnforceAppendOnly: AuditLog is append-only.
        // Even though the logger is the only legitimate writer, downstream
        // code attempting UPDATE/DELETE must still trip the guard.
        var logger = CreateLogger();
        await logger.LogAsync(new AuditLogEntry(
            UserId: _admin.Id,
            ActorId: _admin.Id,
            ActorType: ActorType.ADMIN,
            Action: AuditAction.MANUAL_REFUND,
            EntityType: "Transaction",
            EntityId: Guid.NewGuid().ToString(),
            OldValue: null,
            NewValue: "{\"refunded\":true}",
            IpAddress: null), CancellationToken.None);
        await Context.SaveChangesAsync();

        var tracked = await Context.Set<AuditLog>().OrderByDescending(a => a.Id).FirstAsync();

        tracked.NewValue = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Context.SaveChangesAsync());
        Context.Entry(tracked).State = EntityState.Detached;

        var freshCtx = CreateContext();
        var fresh = await freshCtx.Set<AuditLog>().FirstAsync(a => a.Id == tracked.Id);
        freshCtx.Set<AuditLog>().Remove(fresh);
        await Assert.ThrowsAsync<InvalidOperationException>(() => freshCtx.SaveChangesAsync());
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTime utcNow) => _now = new DateTimeOffset(utcNow, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
