using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Skinora.Shared.Enums;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;
using static Skinora.API.Tests.Integration.NetworkDimension.NetworkDimensionTestSupport;

namespace Skinora.API.Tests.Integration.NetworkDimension;

/// <summary>
/// T157 — migration rehearsal for <c>T157_OutboundRowClosures</c> on real SQL
/// Server (06 §3.8b): the table, its CHECKs and its two filtered unique indexes
/// on a clean database; an existing open Ethereum outbound row is untouched by
/// rolling forward; Down is refused while closure records exist (they are the
/// working record of a two-person money decision) and drops the empty table
/// otherwise.
/// </summary>
public class OutboundRowClosureMigrationRehearsalTests : IntegrationTestBase
{
    private const string T157Suffix = "_T157_OutboundRowClosures";

    protected override bool UseMigrations => true;

    static OutboundRowClosureMigrationRehearsalTests() => RegisterAllModules();

    private string MigrationBeforeT157()
    {
        var all = Context.Database.GetMigrations().ToList();
        var index = all.FindIndex(m => m.EndsWith(T157Suffix, StringComparison.Ordinal));
        Assert.True(index > 0, "T157 migration not found in the assembly.");
        return all[index - 1];
    }

    private async Task MigrateToAsync(string? target)
    {
        await using var ctx = CreateContext();
        await ctx.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task<List<string?>> QueryAsync(string sql)
    {
        await using var ctx = CreateContext();
        return await ctx.Database.SqlQueryRaw<string?>(sql).ToListAsync();
    }

    private async Task<(User Admin, BlockchainTransaction Row)> SeedOpenRowAsync(string steamId)
    {
        await using var ctx = CreateContext();
        var seller = new User { Id = Guid.NewGuid(), SteamId = steamId, SteamDisplayName = "T157 rehearsal" };
        var tx = NewTransaction(seller.Id, network: BlockchainNetwork.ETHEREUM);
        var row = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Network = BlockchainNetwork.ETHEREUM,
            Type = BlockchainTransactionType.SELLER_PAYOUT,
            FromAddress = EvmChecksummed2,
            ToAddress = EvmChecksummed,
            Amount = 50m,
            Token = StablecoinType.USDT,
            Status = BlockchainTransactionStatus.PENDING,
            CreatedAt = DateTime.UtcNow,
        };
        ctx.Set<User>().Add(seller);
        ctx.Set<Transaction>().Add(tx);
        await ctx.SaveChangesAsync();
        ctx.Set<BlockchainTransaction>().Add(row);
        await ctx.SaveChangesAsync();
        return (seller, row);
    }

    [Fact]
    public async Task CleanDatabase_TableChecksAndFilteredUniqueIndexesHaveTheTargetShape()
    {
        var applied = await Context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.EndsWith(T157Suffix, StringComparison.Ordinal));

        Assert.Single(await QueryAsync("SELECT name AS Value FROM sys.tables WHERE name = 'OutboundRowClosures'"));

        var checks = await QueryAsync(
            "SELECT name AS Value FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('OutboundRowClosures')");
        Assert.Contains("CK_OutboundRowClosures_Network_Ethereum", checks);
        Assert.Contains("CK_OutboundRowClosures_Approved_DifferentAdmin", checks);
        Assert.Contains("CK_OutboundRowClosures_Decision_Fields", checks);

        var indexes = await QueryAsync(@"
            SELECT CONCAT(name, '|', is_unique, '|', filter_definition) AS Value
            FROM sys.indexes WHERE object_id = OBJECT_ID('OutboundRowClosures') AND name LIKE 'UQ_%'");
        Assert.Equal(2, indexes.Count);
        Assert.All(indexes, i => Assert.Contains("|1|", i));
        Assert.All(indexes, i => Assert.Contains("REJECTED", i));
    }

    [Fact]
    public async Task ExistingOpenRow_IsUntouchedByRollingForward()
    {
        var (_, row) = await SeedOpenRowAsync("76561198000001601");

        await MigrateToAsync(MigrationBeforeT157());
        Assert.Empty(await QueryAsync("SELECT name AS Value FROM sys.tables WHERE name = 'OutboundRowClosures'"));

        await MigrateToAsync(null);

        Assert.Equal("PENDING|-", Assert.Single(await QueryAsync(
            $"SELECT CONCAT(Status, '|', COALESCE(TxHash, '-')) AS Value FROM BlockchainTransactions WHERE Id = '{row.Id}'")));
        Assert.Equal("0", Assert.Single(await QueryAsync(
            "SELECT CAST(COUNT(*) AS nvarchar(10)) AS Value FROM OutboundRowClosures")));
    }

    [Fact]
    public async Task Down_WhenEmpty_DropsTheTable_AndForwardAgain()
    {
        await MigrateToAsync(MigrationBeforeT157());
        Assert.Empty(await QueryAsync("SELECT name AS Value FROM sys.tables WHERE name = 'OutboundRowClosures'"));

        await MigrateToAsync(null);
        Assert.Single(await QueryAsync("SELECT name AS Value FROM sys.tables WHERE name = 'OutboundRowClosures'"));
    }

    [Fact]
    public async Task Down_WithAClosureRecord_IsRefused_AndChangesNothing()
    {
        var (admin, row) = await SeedOpenRowAsync("76561198000001602");
        await using (var ctx = CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync($@"
                INSERT INTO OutboundRowClosures
                    (Id, BlockchainTransactionId, Network, TxHash, Reason, Status, ProposedByAdminId, CreatedAt, UpdatedAt)
                VALUES (NEWID(), '{row.Id}', 'ETHEREUM', '0x{new string('a', 64)}', 'rehearsal proposal', 'PROPOSED',
                        '{admin.Id}', SYSUTCDATETIME(), SYSUTCDATETIME())");
        }

        var ex = await Assert.ThrowsAsync<SqlException>(() => MigrateToAsync(MigrationBeforeT157()));
        Assert.Equal(50157, ex.Number);

        var applied = await Context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.EndsWith(T157Suffix, StringComparison.Ordinal));
        Assert.Equal("1", Assert.Single(await QueryAsync(
            "SELECT CAST(COUNT(*) AS nvarchar(10)) AS Value FROM OutboundRowClosures")));
    }
}
