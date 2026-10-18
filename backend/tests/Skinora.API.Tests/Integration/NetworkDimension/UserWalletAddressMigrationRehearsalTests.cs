using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Skinora.Shared.Enums;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Domain.Entities;
using static Skinora.API.Tests.Integration.NetworkDimension.NetworkDimensionTestSupport;

namespace Skinora.API.Tests.Integration.NetworkDimension;

/// <summary>
/// T146 — migration rehearsal for <c>T146_UserWalletAddresses</c> on real SQL
/// Server (06 §1.4, §3.1a): the pre-F8 <c>Users.DefaultPayoutAddress</c> /
/// <c>DefaultRefundAddress</c> / <c>*ChangedAt</c> columns become
/// <c>UserWalletAddresses</c> rows with <c>Network = TRON</c>.
/// <list type="bullet">
///   <item><description><b>Clean database</b>: every migration applied to an
///   empty database; the table, its keys and the dropped columns are asserted.</description></item>
///   <item><description><b>Existing addresses</b>: users are written, the
///   database is taken back to the migration before T146 (the pre-T146 schema
///   with the four <c>Users</c> columns), the columns are filled the way the
///   old code filled them, and T146 is applied again over those rows.</description></item>
///   <item><description><b>Down</b>: TRON rows go back into the columns; a
///   non-TRON row has nowhere to go, so rolling back with one present fails
///   and changes nothing.</description></item>
/// </list>
/// </summary>
public class UserWalletAddressMigrationRehearsalTests : IntegrationTestBase
{
    private const string T146Suffix = "_T146_UserWalletAddresses";

    private const string TronPayout = "TWHPPkNRxo1ovxC2uCWmyoir9AiWpt4F2H";
    private const string TronRefund = "THLYcxdu71TwB4HWoQFkMfrhjw5tgH2QkE";
    private const string TronPayout2 = "TF5hehHaDaMzgAJVZeZg8AyjLKnVxmJ4Hu";

    protected override bool UseMigrations => true;

    static UserWalletAddressMigrationRehearsalTests() => RegisterAllModules();

    private string MigrationBeforeT146()
    {
        var all = Context.Database.GetMigrations().ToList();
        var index = all.FindIndex(m => m.EndsWith(T146Suffix, StringComparison.Ordinal));
        Assert.True(index > 0, "T146 migration not found in the assembly.");
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

    private async Task ExecuteAsync(string sql)
    {
        await using var ctx = CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(sql);
    }

    private static User NewUser(string steamId) => new()
    {
        Id = Guid.NewGuid(),
        SteamId = steamId,
        SteamDisplayName = "U" + steamId[^3..],
        CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
    };

    // ===================== Clean database =====================

    [Fact]
    public async Task CleanDatabase_T146IsApplied_TableHasTheTargetShape_UserColumnsAreGone()
    {
        var applied = await Context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.EndsWith(T146Suffix, StringComparison.Ordinal));

        var columns = await QueryAsync(@"
            SELECT CONCAT(c.name, ':', ty.name, ':', c.is_nullable, ':', c.max_length) AS Value
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE t.name = 'UserWalletAddresses'");
        Assert.Contains("Network:nvarchar:0:40", columns);
        Assert.Contains("Type:nvarchar:0:20", columns);
        Assert.Contains("Address:nvarchar:0:100", columns);
        Assert.Contains("ChangedAt:datetime2:1:8", columns);
        Assert.Contains("UserId:uniqueidentifier:0:16", columns);

        var keys = await QueryAsync(@"
            SELECT CONCAT(i.name, '=', i.is_unique, '=', STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)) AS Value
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('UserWalletAddresses') AND i.is_primary_key = 0
            GROUP BY i.name, i.is_unique");
        Assert.Contains("UQ_UserWalletAddresses_UserId_Network_Type=1=UserId,Network,Type", keys);
        Assert.Contains("IX_UserWalletAddresses_Network_Address=0=Network,Address", keys);

        Assert.Equal("FK_UserWalletAddresses_Users_UserId", Assert.Single(await QueryAsync(
            "SELECT name AS Value FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('UserWalletAddresses')")));

        Assert.Empty(await QueryAsync(@"
            SELECT c.name AS Value FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
            WHERE t.name = 'Users' AND c.name IN
                ('DefaultPayoutAddress', 'DefaultRefundAddress', 'PayoutAddressChangedAt', 'RefundAddressChangedAt')"));

        // No database default — a row without a network or a type cannot be written.
        Assert.Empty(await QueryAsync(@"
            SELECT c.name AS Value FROM sys.default_constraints d
            JOIN sys.columns c ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id
            WHERE d.parent_object_id = OBJECT_ID('UserWalletAddresses')"));
    }

    [Fact]
    public async Task CleanDatabase_OneAddressPerUserNetworkAndType()
    {
        var user = NewUser("76561198000000941");
        user.WalletAddresses.Add(UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, TronPayout));
        user.WalletAddresses.Add(UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.REFUND, TronRefund));
        user.WalletAddresses.Add(UserWalletAddress.For(BlockchainNetwork.ETHEREUM, WalletAddressType.PAYOUT, EvmLower));
        await using (var ctx = CreateContext())
        {
            ctx.Set<User>().Add(user);
            await ctx.SaveChangesAsync();
        }

        // A second TRON payout address for the same user violates the key.
        await using var dup = CreateContext();
        var second = UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, TronPayout2);
        second.UserId = user.Id;
        dup.Set<UserWalletAddress>().Add(second);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());
        Assert.Contains("UQ_UserWalletAddresses_UserId_Network_Type", ex.InnerException!.Message);
    }

    // ===================== Existing addresses: pre-T146 schema → T146 =====================

    [Fact]
    public async Task ExistingAddresses_PreT146SchemaRolledForward_BecomeTronRows()
    {
        // ---- Arrange: users written on today's schema, no addresses yet ----
        var both = NewUser("76561198000000951");          // payout + refund, both timed
        var payoutOnly = NewUser("76561198000000952");    // payout set before change tracking (NULL time)
        var refundOnly = NewUser("76561198000000953");
        var blank = NewUser("76561198000000954");         // empty / whitespace strings are not addresses
        var none = NewUser("76561198000000955");
        var deactivated = NewUser("76561198000000956");   // kept: the address stays the user's data
        deactivated.IsDeactivated = true;
        deactivated.DeactivatedAt = DateTime.UtcNow;

        await using (var ctx = CreateContext())
        {
            ctx.Set<User>().AddRange(both, payoutOnly, refundOnly, blank, none, deactivated);
            await ctx.SaveChangesAsync();
        }

        // ---- Back to the pre-T146 schema and fill the columns as the old code did ----
        await MigrateToAsync(MigrationBeforeT146());
        Assert.Empty(await QueryAsync("SELECT name AS Value FROM sys.tables WHERE name = 'UserWalletAddresses'"));

        await ExecuteAsync($@"
            UPDATE Users SET DefaultPayoutAddress = '{TronPayout}', PayoutAddressChangedAt = '2026-09-01T10:00:00',
                             DefaultRefundAddress = '{TronRefund}', RefundAddressChangedAt = '2026-09-02T11:00:00'
                WHERE Id = '{both.Id}';
            UPDATE Users SET DefaultPayoutAddress = '{TronPayout2}' WHERE Id = '{payoutOnly.Id}';
            UPDATE Users SET DefaultRefundAddress = '{TronRefund}', RefundAddressChangedAt = '2026-09-03T12:00:00'
                WHERE Id = '{refundOnly.Id}';
            UPDATE Users SET DefaultPayoutAddress = '', DefaultRefundAddress = '   ' WHERE Id = '{blank.Id}';
            UPDATE Users SET DefaultPayoutAddress = '{TronPayout}' WHERE Id = '{deactivated.Id}';");

        // ---- Act: the T146 forward migration over existing rows ----
        await MigrateToAsync(null);

        // ---- Assert ----
        await using var read = CreateContext();
        var rows = await read.Set<UserWalletAddress>().AsNoTracking().ToListAsync();
        Assert.All(rows, x => Assert.Equal(BlockchainNetwork.TRON, x.Network));
        Assert.Equal(5, rows.Count);

        UserWalletAddress Row(User u, WalletAddressType t) => Assert.Single(rows, x => x.UserId == u.Id && x.Type == t);

        var payout = Row(both, WalletAddressType.PAYOUT);
        Assert.Equal(TronPayout, payout.Address);
        Assert.Equal(new DateTime(2026, 9, 1, 10, 0, 0), payout.ChangedAt);
        Assert.Equal(new DateTime(2026, 9, 1, 10, 0, 0), payout.CreatedAt);
        var refund = Row(both, WalletAddressType.REFUND);
        Assert.Equal(TronRefund, refund.Address);
        Assert.Equal(new DateTime(2026, 9, 2, 11, 0, 0), refund.ChangedAt);

        // NULL change time stays NULL — no cooldown appears out of nowhere;
        // CreatedAt falls back to the user's creation.
        var legacy = Row(payoutOnly, WalletAddressType.PAYOUT);
        Assert.Equal(TronPayout2, legacy.Address);
        Assert.Null(legacy.ChangedAt);
        Assert.Equal(payoutOnly.CreatedAt, legacy.CreatedAt);

        Assert.Equal(TronRefund, Row(refundOnly, WalletAddressType.REFUND).Address);
        Assert.Equal(TronPayout, Row(deactivated, WalletAddressType.PAYOUT).Address);
        Assert.DoesNotContain(rows, x => x.UserId == blank.Id || x.UserId == none.Id);

        // The same address on two accounts survives (multi-account evidence).
        Assert.Equal(2, rows.Count(x => x.Address == TronPayout));

        // And the old columns are gone again.
        Assert.Empty(await QueryAsync(@"
            SELECT c.name AS Value FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
            WHERE t.name = 'Users' AND c.name LIKE '%AddressChangedAt'"));
    }

    // ===================== Down =====================

    [Fact]
    public async Task Down_WithTronRowsOnly_PutsThemBackIntoTheUserColumns()
    {
        var user = NewUser("76561198000000961");
        user.WalletAddresses.Add(UserWalletAddress.For(
            BlockchainNetwork.TRON, WalletAddressType.PAYOUT, TronPayout, new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc)));
        user.WalletAddresses.Add(UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.REFUND, TronRefund));
        await using (var ctx = CreateContext())
        {
            ctx.Set<User>().Add(user);
            await ctx.SaveChangesAsync();
        }

        await MigrateToAsync(MigrationBeforeT146());

        Assert.Equal(
            $"{TronPayout}|2026-09-05 08:00|{TronRefund}|-",
            Assert.Single(await QueryAsync($@"
                SELECT CONCAT(DefaultPayoutAddress, '|', FORMAT(PayoutAddressChangedAt, 'yyyy-MM-dd HH:mm'), '|',
                              DefaultRefundAddress, '|', COALESCE(FORMAT(RefundAddressChangedAt, 'yyyy-MM-dd HH:mm'), '-')) AS Value
                FROM Users WHERE Id = '{user.Id}'")));
    }

    [Fact]
    public async Task Down_WithAnEthereumAddress_IsRefused_AndChangesNothing()
    {
        var user = NewUser("76561198000000971");
        user.WalletAddresses.Add(UserWalletAddress.For(BlockchainNetwork.ETHEREUM, WalletAddressType.REFUND, EvmLower));
        await using (var ctx = CreateContext())
        {
            ctx.Set<User>().Add(user);
            await ctx.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<SqlException>(() => MigrateToAsync(MigrationBeforeT146()));
        Assert.Equal(50146, ex.Number);

        var applied = await Context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.EndsWith(T146Suffix, StringComparison.Ordinal));
        Assert.Equal("ETHEREUM", Assert.Single(await QueryAsync("SELECT Network AS Value FROM UserWalletAddresses")));
    }
}
