using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.API.Monitoring;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;

namespace Skinora.API.Tests.Integration.Monitoring;

/// <summary>
/// T164 — the probe's two queries against a real SQL Server: the data/log
/// sizes of the current database, the edition, the recovery model and the age
/// of the newest scheduled (non copy-only) full and log backup in msdb.
/// </summary>
public sealed class SqlServerDatabaseStorageProbeTests : IntegrationTestBase
{
    protected override Task SeedAsync(AppDbContext context) => Task.CompletedTask;

    private SqlServerDatabaseStorageProbe Probe() =>
        new(Context, NullLogger<SqlServerDatabaseStorageProbe>.Instance);

    // Database names come from IntegrationTestBase, never from input; passing
    // the SQL as a plain string keeps EF1002 quiet without a suppression.
    private Task<int> ExecAsync(string sql) => Context.Database.ExecuteSqlRawAsync(sql);

    private async Task<string> DatabaseNameAsync() =>
        (await Context.Database.SqlQueryRaw<string>("SELECT DB_NAME() AS [Value]").ToListAsync()).Single();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Reads_Sizes_Edition_And_Recovery_Model_Of_The_Current_Database()
    {
        var db = await DatabaseNameAsync();
        await ExecAsync($"ALTER DATABASE [{db}] SET RECOVERY SIMPLE");

        var snapshot = await Probe().ReadAsync(CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.InRange(snapshot!.EngineEdition, 1, 12);
        Assert.Equal("SIMPLE", snapshot.RecoveryModel);
        Assert.True(snapshot.DataAllocatedBytes > 0);
        Assert.InRange(snapshot.DataUsedBytes, 1, snapshot.DataAllocatedBytes);
        Assert.True(snapshot.LogAllocatedBytes > 0);
        Assert.True(snapshot.BackupHistoryRead);
        Assert.Null(snapshot.FullBackupAgeSeconds);
        Assert.Null(snapshot.LogBackupAgeSeconds);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Counts_Scheduled_Backups_Only_Copy_Only_Is_Ignored()
    {
        var db = await DatabaseNameAsync();
        await ExecAsync($"ALTER DATABASE [{db}] SET RECOVERY FULL");

        // A copy-only backup is an operator's ad-hoc copy, not the schedule.
        await ExecAsync(
            $"BACKUP DATABASE [{db}] TO DISK = N'{db}_copy.bak' WITH COPY_ONLY, INIT");
        var afterCopy = await Probe().ReadAsync(CancellationToken.None);
        Assert.Null(afterCopy!.FullBackupAgeSeconds);

        await ExecAsync($"BACKUP DATABASE [{db}] TO DISK = N'{db}_full.bak' WITH INIT");
        await ExecAsync($"BACKUP LOG [{db}] TO DISK = N'{db}_log.trn' WITH INIT");

        var snapshot = await Probe().ReadAsync(CancellationToken.None);

        Assert.Equal("FULL", snapshot!.RecoveryModel);
        Assert.InRange(snapshot.FullBackupAgeSeconds!.Value, 0, 120);
        Assert.InRange(snapshot.LogBackupAgeSeconds!.Value, 0, 120);
    }
}
