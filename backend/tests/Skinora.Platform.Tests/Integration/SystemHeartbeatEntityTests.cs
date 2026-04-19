using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;

namespace Skinora.Platform.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="SystemHeartbeat"/> (T25, 06 §3.23).
/// The singleton Id = 1 row is provided by the T26 seed contract (06 §8.9),
/// so these tests only exercise UPDATE and the second-row rejection path.
/// </summary>
public class SystemHeartbeatEntityTests : IntegrationTestBase
{
    static SystemHeartbeatEntityTests()
    {
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemHeartbeat_SeedRow_IsPresent()
    {
        var loaded = await Context.Set<SystemHeartbeat>().FirstAsync();
        Assert.Equal(SeedConstants.SystemHeartbeatId, loaded.Id);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemHeartbeat_SecondRow_RejectedBy_CheckConstraint()
    {
        // 06 §3.23: CHECK (Id = 1) forbids any row whose Id is not 1.
        Context.Set<SystemHeartbeat>().Add(new SystemHeartbeat
        {
            Id = 2,
            LastHeartbeat = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemHeartbeat_Update_RefreshesTimestamp()
    {
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
