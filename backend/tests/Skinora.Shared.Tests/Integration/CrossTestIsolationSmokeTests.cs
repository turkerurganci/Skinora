using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;

namespace Skinora.Shared.Tests.Integration;

/// <summary>
/// T11.3 kabul kriteri — two independent test classes (different xUnit test
/// collections, eligible to run in parallel) both write the same primary key
/// to the same table. With per-class unique databases the writes must not
/// collide. Also asserts each class sees its own <c>DB_NAME()</c>, proving
/// the shared server is partitioned per class.
/// </summary>
public class CrossTestIsolationSmokeTestsA : IntegrationTestBase
{
    // Fixed GUID shared with class B — collision would only occur if both
    // classes landed in the same physical database.
    internal static readonly Guid SharedId = Guid.Parse("c0c0c0c0-1111-1111-1111-111111111111");

    [Fact]
    public async Task Writes_SharedFixedId_IntoOwnDatabase_WithoutCollision()
    {
        var dbName = await GetCurrentDatabaseNameAsync(Context);
        Assert.StartsWith("T_", dbName);
        Assert.Contains(nameof(CrossTestIsolationSmokeTestsA), dbName);

        var message = new OutboxMessage
        {
            Id = SharedId,
            EventType = "IsolationSmokeA",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow
        };

        Context.OutboxMessages.Add(message);
        await Context.SaveChangesAsync();

        var loaded = await Context.OutboxMessages.FirstAsync(m => m.Id == SharedId);
        Assert.Equal("IsolationSmokeA", loaded.EventType);
    }

    internal static async Task<string> GetCurrentDatabaseNameAsync(AppDbContext ctx)
    {
        var rows = await ctx.Database
            .SqlQueryRaw<string>("SELECT DB_NAME() AS [Value]")
            .ToListAsync();
        return rows.FirstOrDefault() ?? string.Empty;
    }
}

public class CrossTestIsolationSmokeTestsB : IntegrationTestBase
{
    [Fact]
    public async Task Writes_SharedFixedId_IntoOwnDatabase_WithoutCollision()
    {
        var dbName = await CrossTestIsolationSmokeTestsA.GetCurrentDatabaseNameAsync(Context);
        Assert.StartsWith("T_", dbName);
        Assert.Contains(nameof(CrossTestIsolationSmokeTestsB), dbName);

        var message = new OutboxMessage
        {
            Id = CrossTestIsolationSmokeTestsA.SharedId,
            EventType = "IsolationSmokeB",
            Payload = "{}",
            CreatedAt = DateTime.UtcNow
        };

        Context.OutboxMessages.Add(message);
        await Context.SaveChangesAsync();

        var loaded = await Context.OutboxMessages.FirstAsync(m => m.Id == CrossTestIsolationSmokeTestsA.SharedId);
        Assert.Equal("IsolationSmokeB", loaded.EventType);
    }
}
