using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;

namespace Skinora.Platform.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="SystemHeartbeat"/> (T25, 06 §3.23).
/// Verifies singleton CHECK (Id = 1) and update-only semantics.
/// </summary>
public class SystemHeartbeatEntityTests : IntegrationTestBase
{
    static SystemHeartbeatEntityTests()
    {
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemHeartbeat_SingletonInsert_Succeeds()
    {
        var hb = new SystemHeartbeat
        {
            Id = 1,
            LastHeartbeat = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        Context.Set<SystemHeartbeat>().Add(hb);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<SystemHeartbeat>().FirstAsync();
        Assert.Equal(1, loaded.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemHeartbeat_SecondRow_RejectedBy_CheckConstraint()
    {
        // 06 §3.23: Id = 1 CHECK ensures the table is a singleton. The second
        // row is rejected by the CHECK (and also by the PK if Id = 1 is
        // reused), so the write fails regardless of the clash type.
        Context.Set<SystemHeartbeat>().Add(new SystemHeartbeat
        {
            Id = 1,
            LastHeartbeat = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        await using var ctx = CreateContext();
        ctx.Set<SystemHeartbeat>().Add(new SystemHeartbeat
        {
            Id = 2,
            LastHeartbeat = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemHeartbeat_Update_RefreshesTimestamp()
    {
        var initial = new SystemHeartbeat
        {
            Id = 1,
            LastHeartbeat = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        Context.Set<SystemHeartbeat>().Add(initial);
        await Context.SaveChangesAsync();

        var tracked = await Context.Set<SystemHeartbeat>().FirstAsync();
        var newTime = DateTime.UtcNow;
        tracked.LastHeartbeat = newTime;
        tracked.UpdatedAt = newTime;
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<SystemHeartbeat>().FirstAsync();
        Assert.Equal(newTime, loaded.LastHeartbeat, TimeSpan.FromMilliseconds(100));
    }
}
