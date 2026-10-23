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
/// T149 — migration rehearsal for <c>T149_NetworkFeePolicy</c> on real SQL
/// Server (06 §1.4, §3.5, §3.17).
/// <list type="bullet">
///   <item><description><b>Clean database</b>: <c>Transactions.NetworkFeeAmount</c>
///   decimal(18,6) NULL, <c>CK_Transactions_NetworkFeeAmount_NonNegative</c>, and
///   the three new settings with their seeded values.</description></item>
///   <item><description><b>Existing transactions</b>: rows are written, the
///   database is taken back to the migration before T149 (no column) and T149 is
///   applied again over them — an accepted row gets an explicit 0 line, an open
///   listing stays NULL, no total changes.</description></item>
///   <item><description><b>Down</b>: with only zero / NULL lines it rolls back;
///   with a positive line (part of a total and a deposit's expected amount) it is
///   refused and changes nothing.</description></item>
/// </list>
/// </summary>
public class NetworkFeePolicyMigrationRehearsalTests : IntegrationTestBase
{
    private const string T149Suffix = "_T149_NetworkFeePolicy";

    protected override bool UseMigrations => true;

    static NetworkFeePolicyMigrationRehearsalTests() => RegisterAllModules();

    private string MigrationBeforeT149()
    {
        var all = Context.Database.GetMigrations().ToList();
        var index = all.FindIndex(m => m.EndsWith(T149Suffix, StringComparison.Ordinal));
        Assert.True(index > 0, "T149 migration not found in the assembly.");
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

    private static User NewSeller(string steamId) => new()
    {
        Id = Guid.NewGuid(),
        SteamId = steamId,
        SteamDisplayName = "S" + steamId[^3..],
    };

    private static Transaction Accepted(Guid sellerId, decimal? networkFee, decimal total)
    {
        var tx = NewTransaction(sellerId, TransactionStatus.ACCEPTED, BlockchainNetwork.TRON);
        tx.AcceptedAt = DateTime.UtcNow;
        tx.NetworkFeeAmount = networkFee;
        tx.TotalAmount = total;
        return tx;
    }

    private async Task SaveAsync(User seller, params Transaction[] transactions)
    {
        await using var ctx = CreateContext();
        ctx.Set<User>().Add(seller);
        ctx.Set<Transaction>().AddRange(transactions);
        await ctx.SaveChangesAsync();
    }

    // ===================== Clean database =====================

    [Fact]
    public async Task CleanDatabase_T149IsApplied_ColumnCheckAndSettingsHaveTheTargetShape()
    {
        var applied = await Context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.EndsWith(T149Suffix, StringComparison.Ordinal));

        Assert.Equal("decimal:1:18:6", Assert.Single(await QueryAsync(@"
            SELECT CONCAT(ty.name, ':', c.is_nullable, ':', c.precision, ':', c.scale) AS Value
            FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.object_id = OBJECT_ID('Transactions') AND c.name = 'NetworkFeeAmount'")));
        Assert.Single(await QueryAsync(
            "SELECT name AS Value FROM sys.check_constraints WHERE name = 'CK_Transactions_NetworkFeeAmount_NonNegative'"));

        var settings = await QueryAsync(@"
            SELECT CONCAT([Key], '=', [Value], '=', IsConfigured) AS Value FROM SystemSettings
            WHERE [Key] IN ('blockchain.tron.network_fee_buffer_ratio',
                            'blockchain.tron.network_fee_fallback_usdt',
                            'blockchain.ethereum.min_refund_threshold_ratio')");
        Assert.Equal(3, settings.Count);
        Assert.Contains("blockchain.tron.network_fee_buffer_ratio=0.007=1", settings);
        Assert.Contains("blockchain.tron.network_fee_fallback_usdt=3.00=1", settings);
        Assert.Contains("blockchain.ethereum.min_refund_threshold_ratio=2.0=1", settings);
        // 75 after T149 + 2 T169 rows (TRON minimum price, outbound launch gate)
        // + 2 T169a rows (deposit refund daily limit per network).
        Assert.Equal("79", Assert.Single(await QueryAsync(
            "SELECT CAST(COUNT(*) AS nvarchar(10)) AS Value FROM SystemSettings")));
    }

    [Fact]
    public async Task CleanDatabase_A_Negative_Line_Is_Refused()
    {
        var seller = NewSeller("76561198000001491");
        var tx = Accepted(seller.Id, networkFee: -0.01m, total: 50.99m);

        await using var ctx = CreateContext();
        ctx.Set<User>().Add(seller);
        ctx.Set<Transaction>().Add(tx);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("CK_Transactions_NetworkFeeAmount_NonNegative", ex.InnerException!.Message);
    }

    [Fact]
    public async Task CleanDatabase_The_Line_Is_Stored_To_The_Cent()
    {
        var seller = NewSeller("76561198000001492");
        var tx = Accepted(seller.Id, networkFee: 2.90m, total: 53.90m);
        await SaveAsync(seller, tx);

        Assert.Equal("2.900000|53.900000", Assert.Single(await QueryAsync(
            $"SELECT CONCAT(NetworkFeeAmount, '|', TotalAmount) AS Value FROM Transactions WHERE Id = '{tx.Id}'")));
    }

    // ===================== Existing transactions: pre-T149 schema → T149 =====================

    [Fact]
    public async Task ExistingTransactions_PreT149SchemaRolledForward_AcceptedGetZero_ListingsStayNull()
    {
        var seller = NewSeller("76561198000001493");
        var listing = NewTransaction(seller.Id);                         // CREATED: line not fixed yet
        var accepted = Accepted(seller.Id, networkFee: null, total: 51m); // accepted before T149
        await SaveAsync(seller, listing, accepted);

        await MigrateToAsync(MigrationBeforeT149());
        Assert.Empty(await QueryAsync(@"
            SELECT c.name AS Value FROM sys.columns c
            WHERE c.object_id = OBJECT_ID('Transactions') AND c.name = 'NetworkFeeAmount'"));
        Assert.Empty(await QueryAsync(
            "SELECT [Key] AS Value FROM SystemSettings WHERE [Key] LIKE 'blockchain.tron.%' OR [Key] = 'blockchain.ethereum.min_refund_threshold_ratio'"));

        await MigrateToAsync(null);

        var rows = await QueryAsync(
            "SELECT CONCAT(Id, '|', COALESCE(CAST(NetworkFeeAmount AS nvarchar(30)), '-'), '|', TotalAmount) AS Value FROM Transactions");
        Assert.Contains($"{accepted.Id}|0.000000|51.000000", rows, StringComparer.OrdinalIgnoreCase);
        Assert.Contains($"{listing.Id}|-|51.000000", rows, StringComparer.OrdinalIgnoreCase);
    }

    // ===================== Down =====================

    [Fact]
    public async Task Down_WithOnlyZeroOrNullLines_RollsBack_AndForwardAgain()
    {
        var seller = NewSeller("76561198000001494");
        await SaveAsync(seller, NewTransaction(seller.Id), Accepted(seller.Id, networkFee: 0m, total: 51m));

        await MigrateToAsync(MigrationBeforeT149());
        var applied = await Context.Database.GetAppliedMigrationsAsync();
        Assert.DoesNotContain(applied, m => m.EndsWith(T149Suffix, StringComparison.Ordinal));

        await MigrateToAsync(null);
    }

    [Fact]
    public async Task Down_WithAPositiveLine_IsRefused_AndChangesNothing()
    {
        var seller = NewSeller("76561198000001495");
        var tx = Accepted(seller.Id, networkFee: 2.90m, total: 53.90m);
        await SaveAsync(seller, tx);

        var ex = await Assert.ThrowsAsync<SqlException>(() => MigrateToAsync(MigrationBeforeT149()));
        Assert.Equal(50149, ex.Number);

        var applied = await Context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.EndsWith(T149Suffix, StringComparison.Ordinal));
        Assert.Equal("2.900000", Assert.Single(await QueryAsync(
            $"SELECT CAST(NetworkFeeAmount AS nvarchar(30)) AS Value FROM Transactions WHERE Id = '{tx.Id}'")));
        Assert.Single(await QueryAsync(
            "SELECT [Key] AS Value FROM SystemSettings WHERE [Key] = 'blockchain.tron.network_fee_buffer_ratio'"));
    }
}
