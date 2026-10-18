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
/// T147 — migration rehearsal for <c>T147_TransactionNetworkOptions</c> on real
/// SQL Server (06 §1.4, §3.5, §3.5b).
/// <list type="bullet">
///   <item><description><b>Clean database</b>: the option table, its key and
///   FK, the nullable payout column and <c>CK_Transactions_Network_Accepted</c>.</description></item>
///   <item><description><b>Existing transactions</b>: rows are written, the
///   database is taken back to the migration before T147 (TRON-only schema,
///   payout column NOT NULL), and T147 is applied again over them — every
///   transaction gets one option on its own network carrying its address.</description></item>
///   <item><description><b>Down</b>: a TRON-only listing's payout address goes
///   back into the column; a non-TRON listing or transaction has nowhere to go,
///   so rolling back with one present fails and changes nothing.</description></item>
/// </list>
/// </summary>
public class TransactionNetworkOptionMigrationRehearsalTests : IntegrationTestBase
{
    private const string T147Suffix = "_T147_TransactionNetworkOptions";

    protected override bool UseMigrations => true;

    static TransactionNetworkOptionMigrationRehearsalTests() => RegisterAllModules();

    private string MigrationBeforeT147()
    {
        var all = Context.Database.GetMigrations().ToList();
        var index = all.FindIndex(m => m.EndsWith(T147Suffix, StringComparison.Ordinal));
        Assert.True(index > 0, "T147 migration not found in the assembly.");
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

    private static TransactionNetworkOption Option(BlockchainNetwork network, string address) => new()
    {
        Id = Guid.NewGuid(),
        Network = network,
        SellerPayoutAddress = address,
        CreatedAt = DateTime.UtcNow,
    };

    // ===================== Clean database =====================

    [Fact]
    public async Task CleanDatabase_T147IsApplied_TableKeysAndCheckHaveTheTargetShape()
    {
        var applied = await Context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.EndsWith(T147Suffix, StringComparison.Ordinal));

        var columns = await QueryAsync(@"
            SELECT CONCAT(c.name, ':', ty.name, ':', c.is_nullable, ':', c.max_length) AS Value
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE t.name = 'TransactionNetworkOptions'");
        Assert.Contains("TransactionId:uniqueidentifier:0:16", columns);
        Assert.Contains("Network:nvarchar:0:40", columns);
        Assert.Contains("SellerPayoutAddress:nvarchar:0:100", columns);
        Assert.Contains("CreatedAt:datetime2:0:8", columns);

        Assert.Equal("UQ_TransactionNetworkOptions_TransactionId_Network=1=TransactionId,Network", Assert.Single(await QueryAsync(@"
            SELECT CONCAT(i.name, '=', i.is_unique, '=', STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)) AS Value
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('TransactionNetworkOptions') AND i.is_primary_key = 0
            GROUP BY i.name, i.is_unique")));
        Assert.Equal("FK_TransactionNetworkOptions_Transactions_TransactionId", Assert.Single(await QueryAsync(
            "SELECT name AS Value FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('TransactionNetworkOptions')")));

        // No database default — an option without a network cannot be written.
        Assert.Empty(await QueryAsync(@"
            SELECT c.name AS Value FROM sys.default_constraints d
            JOIN sys.columns c ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id
            WHERE d.parent_object_id = OBJECT_ID('TransactionNetworkOptions')"));

        Assert.Equal("1", Assert.Single(await QueryAsync(@"
            SELECT CAST(c.is_nullable AS nvarchar(1)) AS Value FROM sys.columns c
            WHERE c.object_id = OBJECT_ID('Transactions') AND c.name = 'SellerPayoutAddress'")));
        Assert.Single(await QueryAsync(
            "SELECT name AS Value FROM sys.check_constraints WHERE name = 'CK_Transactions_Network_Accepted'"));
    }

    [Theory]
    [InlineData(true, false)]   // accepted, no network
    [InlineData(false, true)]   // accepted, network but no payout address
    public async Task CleanDatabase_An_Accepted_Row_Without_Network_Or_Payout_Address_Is_Refused(
        bool payoutAddressSet, bool networkSet)
    {
        var seller = NewSeller("76561198000000961");
        var tx = NewTransaction(seller.Id, TransactionStatus.ACCEPTED, networkSet ? BlockchainNetwork.TRON : null);
        tx.AcceptedAt = DateTime.UtcNow;
        tx.SellerPayoutAddress = payoutAddressSet ? SellerWallet : null;

        await using var ctx = CreateContext();
        ctx.Set<User>().Add(seller);
        ctx.Set<Transaction>().Add(tx);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("CK_Transactions_Network_Accepted", ex.InnerException!.Message);
    }

    [Fact]
    public async Task CleanDatabase_A_Listing_Has_No_Network_And_No_Payout_Column_Until_Accepted()
    {
        var seller = NewSeller("76561198000000962");
        var listing = NewTransaction(seller.Id);
        listing.SellerPayoutAddress = null;
        listing.NetworkOptions.Add(Option(BlockchainNetwork.TRON, SellerWallet));
        listing.NetworkOptions.Add(Option(BlockchainNetwork.ETHEREUM, EvmChecksummed));

        await using (var ctx = CreateContext())
        {
            ctx.Set<User>().Add(seller);
            ctx.Set<Transaction>().Add(listing);
            await ctx.SaveChangesAsync();
        }

        // Canonical form (06 §1.4) on the option column too.
        Assert.Contains(EvmLower, await QueryAsync(
            $"SELECT SellerPayoutAddress AS Value FROM TransactionNetworkOptions WHERE TransactionId = '{listing.Id}'"));

        // A second row for the same network violates the key.
        await using var dup = CreateContext();
        var second = Option(BlockchainNetwork.TRON, SellerWallet);
        second.TransactionId = listing.Id;
        dup.Set<TransactionNetworkOption>().Add(second);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());
        Assert.Contains("UQ_TransactionNetworkOptions_TransactionId_Network", ex.InnerException!.Message);
    }

    // ===================== Existing transactions: pre-T147 schema → T147 =====================

    [Fact]
    public async Task ExistingTransactions_PreT147SchemaRolledForward_GetOneOptionOnTheirOwnNetwork()
    {
        var seller = NewSeller("76561198000000971");
        var listing = NewTransaction(seller.Id);                                       // CREATED, never accepted
        var accepted = NewTransaction(seller.Id, TransactionStatus.ACCEPTED, BlockchainNetwork.TRON);
        accepted.AcceptedAt = DateTime.UtcNow;
        var deleted = NewTransaction(seller.Id);                                       // soft-deleted listing
        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        deleted.SellerPayoutAddress = "TWHPPkNRxo1ovxC2uCWmyoir9AiWpt4F2H";

        await using (var ctx = CreateContext())
        {
            ctx.Set<User>().Add(seller);
            ctx.Set<Transaction>().AddRange(listing, accepted, deleted);
            await ctx.SaveChangesAsync();
        }

        // Back to the TRON-only schema (no option rows, payout column NOT NULL)…
        await MigrateToAsync(MigrationBeforeT147());
        Assert.Empty(await QueryAsync(
            "SELECT name AS Value FROM sys.tables WHERE name = 'TransactionNetworkOptions'"));
        Assert.Equal("0", Assert.Single(await QueryAsync(@"
            SELECT CAST(c.is_nullable AS nvarchar(1)) AS Value FROM sys.columns c
            WHERE c.object_id = OBJECT_ID('Transactions') AND c.name = 'SellerPayoutAddress'")));

        // …and forward again over those rows.
        await MigrateToAsync(null);

        var options = await QueryAsync(
            "SELECT CONCAT(TransactionId, '|', Network, '|', SellerPayoutAddress) AS Value FROM TransactionNetworkOptions");
        Assert.Equal(3, options.Count);
        Assert.Contains($"{listing.Id}|TRON|{SellerWallet}", options, StringComparer.OrdinalIgnoreCase);
        Assert.Contains($"{accepted.Id}|TRON|{SellerWallet}", options, StringComparer.OrdinalIgnoreCase);
        Assert.Contains($"{deleted.Id}|TRON|TWHPPkNRxo1ovxC2uCWmyoir9AiWpt4F2H", options, StringComparer.OrdinalIgnoreCase);

        // Column values are not touched: the accepted row keeps its network and
        // snapshot, the open listing keeps the (identical) pre-T147 value.
        var columns = await QueryAsync(
            "SELECT CONCAT(Id, '|', COALESCE(Network, '-'), '|', SellerPayoutAddress) AS Value FROM Transactions");
        Assert.Contains($"{accepted.Id}|TRON|{SellerWallet}", columns, StringComparer.OrdinalIgnoreCase);
        Assert.Contains($"{listing.Id}|-|{SellerWallet}", columns, StringComparer.OrdinalIgnoreCase);
    }

    // ===================== Down =====================

    [Fact]
    public async Task Down_WithTronListingsOnly_PutsThePayoutAddressBackIntoTheColumn()
    {
        var seller = NewSeller("76561198000000981");
        var listing = NewTransaction(seller.Id);
        listing.SellerPayoutAddress = null;                     // a post-T147 listing
        listing.NetworkOptions.Add(Option(BlockchainNetwork.TRON, SellerWallet));

        await using (var ctx = CreateContext())
        {
            ctx.Set<User>().Add(seller);
            ctx.Set<Transaction>().Add(listing);
            await ctx.SaveChangesAsync();
        }

        await MigrateToAsync(MigrationBeforeT147());

        Assert.Equal(SellerWallet, Assert.Single(await QueryAsync(
            $"SELECT SellerPayoutAddress AS Value FROM Transactions WHERE Id = '{listing.Id}'")));

        await MigrateToAsync(null);
    }

    [Theory]
    [InlineData(false)] // an open listing that also accepts Ethereum
    [InlineData(true)]  // a transaction accepted on Ethereum
    public async Task Down_WithAnEthereumListingOrTransaction_IsRefused_AndChangesNothing(bool acceptedOnEthereum)
    {
        var seller = NewSeller("76561198000000991");
        var tx = acceptedOnEthereum
            ? NewTransaction(seller.Id, TransactionStatus.ACCEPTED, BlockchainNetwork.ETHEREUM)
            : NewTransaction(seller.Id);
        if (acceptedOnEthereum)
        {
            tx.AcceptedAt = DateTime.UtcNow;
            tx.SellerPayoutAddress = EvmChecksummed;
            tx.NetworkOptions.Add(Option(BlockchainNetwork.ETHEREUM, EvmChecksummed));
        }
        else
        {
            tx.SellerPayoutAddress = null;
            tx.NetworkOptions.Add(Option(BlockchainNetwork.TRON, SellerWallet));
            tx.NetworkOptions.Add(Option(BlockchainNetwork.ETHEREUM, EvmChecksummed));
        }

        await using (var ctx = CreateContext())
        {
            ctx.Set<User>().Add(seller);
            ctx.Set<Transaction>().Add(tx);
            await ctx.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<SqlException>(() => MigrateToAsync(MigrationBeforeT147()));
        Assert.Equal(50147, ex.Number);

        var applied = await Context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.EndsWith(T147Suffix, StringComparison.Ordinal));
        Assert.Contains("ETHEREUM", await QueryAsync(
            $"SELECT Network AS Value FROM TransactionNetworkOptions WHERE TransactionId = '{tx.Id}'"));
    }
}
