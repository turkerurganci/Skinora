using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Platform.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="AuditLog"/> (T25, 06 §3.20).
/// Verifies CRUD (insert+read), append-only immutability (UPDATE/DELETE
/// rejection) and enum persistence.
/// </summary>
public class AuditLogEntityTests : IntegrationTestBase
{
    static AuditLogEntityTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private User _actor = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _actor = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000100",
            SteamDisplayName = "AuditActor"
        };
        context.Set<User>().Add(_actor);
        await context.SaveChangesAsync();
    }

    private AuditLog CreateValid()
    {
        return new AuditLog
        {
            UserId = _actor.Id,
            ActorId = _actor.Id,
            ActorType = ActorType.ADMIN,
            Action = AuditAction.WALLET_REFUND,
            EntityType = "Wallet",
            EntityId = Guid.NewGuid().ToString(),
            OldValue = "{\"Balance\": 100}",
            NewValue = "{\"Balance\": 90}",
            IpAddress = "192.168.1.1",
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AuditLog_Insert_And_Read_RoundTrips()
    {
        var log = CreateValid();

        Context.Set<AuditLog>().Add(log);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<AuditLog>().FirstAsync(a => a.Id == log.Id);

        Assert.Equal(ActorType.ADMIN, loaded.ActorType);
        Assert.Equal(AuditAction.WALLET_REFUND, loaded.Action);
        Assert.Equal("Wallet", loaded.EntityType);
        Assert.Equal("192.168.1.1", loaded.IpAddress);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AuditLog_Update_Rejected_By_AppendOnlyGuard()
    {
        // 06 §3.20, §4.2: AuditLog is append-only — UPDATE is not defined
        // and the DbContext throws before EF Core emits SQL.
        var log = CreateValid();
        Context.Set<AuditLog>().Add(log);
        await Context.SaveChangesAsync();

        var tracked = await Context.Set<AuditLog>().FirstAsync(a => a.Id == log.Id);
        tracked.NewValue = "{\"Balance\": 80}";

        await Assert.ThrowsAsync<InvalidOperationException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AuditLog_Delete_Rejected_By_AppendOnlyGuard()
    {
        var log = CreateValid();
        Context.Set<AuditLog>().Add(log);
        await Context.SaveChangesAsync();

        var tracked = await Context.Set<AuditLog>().FirstAsync(a => a.Id == log.Id);
        Context.Set<AuditLog>().Remove(tracked);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AuditLog_System_Event_NullUserId_Allowed()
    {
        var log = CreateValid();
        log.UserId = null;
        log.ActorType = ActorType.SYSTEM;

        Context.Set<AuditLog>().Add(log);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<AuditLog>().FirstAsync(a => a.Id == log.Id);
        Assert.Null(loaded.UserId);
        Assert.Equal(ActorType.SYSTEM, loaded.ActorType);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AuditLog_MultipleInserts_Succeed_Without_Duplication()
    {
        var first = CreateValid();
        var second = CreateValid();

        Context.Set<AuditLog>().Add(first);
        Context.Set<AuditLog>().Add(second);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var count = await readCtx.Set<AuditLog>().CountAsync();
        Assert.Equal(2, count);
        Assert.NotEqual(first.Id, second.Id);
    }
}
