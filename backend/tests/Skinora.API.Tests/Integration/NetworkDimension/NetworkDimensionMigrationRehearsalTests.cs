using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Skinora.Payments.Domain.Entities;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;
using static Skinora.API.Tests.Integration.NetworkDimension.NetworkDimensionTestSupport;

namespace Skinora.API.Tests.Integration.NetworkDimension;

/// <summary>
/// T145 — migration rehearsal for <c>T145_NetworkDimension</c> on real SQL
/// Server (11 §F8 T145: "Tek forward migration; mevcut TRON satırları
/// Network=TRON").
/// <list type="bullet">
///   <item><description><b>Clean database</b>: the base class applies every
///   migration to an empty database; the schema facts are asserted here.</description></item>
///   <item><description><b>Database with existing rows</b>: rows are written,
///   the database is taken back to the migration before T145 (the pre-F8 schema:
///   no Network columns, sanctions network <c>'TRC-20'</c>), and T145 is applied
///   again over those rows. Every row must come out as TRON; an unaccepted
///   transaction must keep a NULL network.</description></item>
///   <item><description><b>Down guard</b>: the pre-F8 schema cannot hold a
///   non-TRON row, so rolling back with one present must fail and change nothing.</description></item>
/// </list>
/// </summary>
public class NetworkDimensionMigrationRehearsalTests : IntegrationTestBase
{
    private const string T145Suffix = "_T145_NetworkDimension";

    protected override bool UseMigrations => true;

    static NetworkDimensionMigrationRehearsalTests() => RegisterAllModules();

    private string PreviousMigration()
    {
        var all = Context.Database.GetMigrations().ToList();
        var index = all.FindIndex(m => m.EndsWith(T145Suffix, StringComparison.Ordinal));
        Assert.True(index > 0, "T145 migration not found in the assembly.");
        return all[index - 1];
    }

    private async Task MigrateToAsync(string? target)
    {
        await using var ctx = CreateContext();
        await ctx.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task<List<string?>> ColumnAsync(string sql)
    {
        await using var ctx = CreateContext();
        return await ctx.Database.SqlQueryRaw<string?>(sql).ToListAsync();
    }

    // ===================== Clean database =====================

    [Fact]
    public async Task CleanDatabase_T145IsApplied_AndNetworkColumnsHaveTheTargetShape()
    {
        var applied = await Context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.EndsWith(T145Suffix, StringComparison.Ordinal));

        var shape = await ColumnAsync(@"
            SELECT CONCAT(t.name, ':', c.is_nullable, ':', c.max_length) AS Value
            FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
            WHERE c.name = 'Network'");

        // nvarchar(20) = 40 bytes. NOT NULL everywhere except Transactions.
        Assert.Contains("PaymentAddresses:0:40", shape);
        Assert.Contains("BlockchainTransactions:0:40", shape);
        Assert.Contains("ColdWalletTransfers:0:40", shape);
        Assert.Contains("SellerPayoutIssues:0:40", shape);
        Assert.Contains("WalletAddressHistory:0:40", shape);
        Assert.Contains("SanctionedAddresses:0:40", shape);
        Assert.Contains("Transactions:1:40", shape);
    }

    [Fact]
    public async Task CleanDatabase_UniqueIndexes_LeadWithTheNetwork()
    {
        var keys = await ColumnAsync(@"
            SELECT CONCAT(i.name, '=', STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)) AS Value
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.is_unique = 1 AND i.name IN (
                'UQ_PaymentAddresses_Network_Address',
                'UQ_PaymentAddresses_Network_HdWalletIndex',
                'UQ_BlockchainTransactions_Network_TxHash_EventIndex',
                'UQ_ColdWalletTransfers_Network_TxHash',
                'UQ_SanctionedAddresses_Network_Address_Active')
            GROUP BY i.name");

        Assert.Equal(5, keys.Count);
        Assert.Contains("UQ_PaymentAddresses_Network_Address=Network,Address", keys);
        Assert.Contains("UQ_PaymentAddresses_Network_HdWalletIndex=Network,HdWalletIndex", keys);
        Assert.Contains("UQ_BlockchainTransactions_Network_TxHash_EventIndex=Network,TxHash,EventIndex", keys);
        Assert.Contains("UQ_ColdWalletTransfers_Network_TxHash=Network,TxHash", keys);
        Assert.Contains("UQ_SanctionedAddresses_Network_Address_Active=Network,Address", keys);

        // The network-less predecessors are gone.
        var old = await ColumnAsync(@"
            SELECT name AS Value FROM sys.indexes WHERE name IN (
                'UQ_PaymentAddresses_Address', 'UQ_PaymentAddresses_HdWalletIndex',
                'UQ_BlockchainTransactions_TxHash_EventIndex', 'UQ_ColdWalletTransfers_TxHash',
                'UQ_SanctionedAddresses_Address_Active')");
        Assert.Empty(old);
    }

    [Fact]
    public async Task CleanDatabase_MoneyColumns_StaySixDecimals()
    {
        var scales = await ColumnAsync(@"
            SELECT CONCAT(t.name, '.', c.name, '=', c.precision, ',', c.scale) AS Value
            FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
            WHERE (t.name = 'PaymentAddresses' AND c.name = 'ExpectedAmount')
               OR (t.name = 'BlockchainTransactions' AND c.name IN ('Amount', 'GasFee'))
               OR (t.name = 'ColdWalletTransfers' AND c.name = 'Amount')
               OR (t.name = 'Transactions' AND c.name IN ('Price', 'CommissionAmount', 'TotalAmount'))");

        Assert.Equal(7, scales.Count);
        Assert.All(scales, s => Assert.EndsWith("=18,6", s));
    }

    // ===================== Existing rows: pre-F8 schema → T145 =====================

    [Fact]
    public async Task ExistingRows_PreF8SchemaRolledForward_AreAllTron()
    {
        // ---- Arrange: one row of every network-bearing kind ----
        var admin = new User { Id = Guid.NewGuid(), SteamId = "76561198000000911", SteamDisplayName = "Admin" };
        var seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000000912", SteamDisplayName = "Seller" };
        var buyer = new User { Id = Guid.NewGuid(), SteamId = "76561198000000913", SteamDisplayName = "Buyer" };

        var accepted = NewTransaction(seller.Id, TransactionStatus.ACCEPTED, BlockchainNetwork.TRON);
        accepted.BuyerId = buyer.Id;
        accepted.AcceptedAt = DateTime.UtcNow;
        accepted.BuyerRefundAddress = TronAddress;

        var unaccepted = NewTransaction(seller.Id);              // CREATED, no network

        var allocatedOnly = NewTransaction(seller.Id);           // CREATED but a deposit exists
        var completed = NewTransaction(seller.Id, TransactionStatus.COMPLETED, BlockchainNetwork.TRON);
        completed.BuyerId = buyer.Id;
        completed.AcceptedAt = DateTime.UtcNow;

        await using (var ctx = CreateContext())
        {
            ctx.Set<User>().AddRange(admin, seller, buyer);
            ctx.Set<Transaction>().AddRange(accepted, unaccepted, allocatedOnly, completed);
            await ctx.SaveChangesAsync();

            var pa = NewPaymentAddress(accepted.Id, BlockchainNetwork.TRON, TronAddress, 0);
            var paAllocated = NewPaymentAddress(allocatedOnly.Id, BlockchainNetwork.TRON, SellerWallet, 1);
            ctx.Set<PaymentAddress>().AddRange(pa, paAllocated);
            await ctx.SaveChangesAsync();

            var confirmed = NewInbound(accepted.Id, pa.Id, BlockchainNetwork.TRON, new string('a', 64));
            confirmed.Status = BlockchainTransactionStatus.CONFIRMED;
            confirmed.ConfirmationCount = 20;
            confirmed.ConfirmedAt = DateTime.UtcNow;
            ctx.Set<BlockchainTransaction>().Add(confirmed);

            ctx.Set<ColdWalletTransfer>().Add(new ColdWalletTransfer
            {
                Amount = 5m,
                Token = StablecoinType.USDT,
                FromAddress = TronAddress,
                ToAddress = SellerWallet,
                TxHash = new string('b', 64),
                InitiatedByAdminId = admin.Id,
                CreatedAt = DateTime.UtcNow,
            });

            ctx.Set<SellerPayoutIssue>().Add(new SellerPayoutIssue
            {
                Id = Guid.NewGuid(),
                TransactionId = completed.Id,
                SellerId = seller.Id,
                Detail = "Payout did not arrive",
                VerificationStatus = PayoutIssueStatus.REPORTED,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });

            ctx.Set<WalletAddressHistory>().Add(new WalletAddressHistory
            {
                UserId = seller.Id,
                Type = WalletAddressHistoryType.Seller,
                Address = TronAddress,
                CreatedAt = DateTime.UtcNow,
            });

            ctx.Set<SanctionedAddress>().Add(new SanctionedAddress
            {
                Id = Guid.NewGuid(),
                Address = SellerWallet,
                Network = BlockchainNetwork.TRON,
                Source = SanctionedAddressSources.Manual,
                ListedAt = DateTime.UtcNow,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        // ---- Back to the pre-F8 schema ----
        await MigrateToAsync(PreviousMigration());

        Assert.Empty(await ColumnAsync(
            "SELECT t.name AS Value FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id WHERE c.name = 'Network' AND t.name <> 'SanctionedAddresses'"));
        Assert.Equal("TRC-20", Assert.Single(await ColumnAsync("SELECT Network AS Value FROM SanctionedAddresses")));

        // ---- Act: the T145 forward migration over existing rows ----
        await MigrateToAsync(null);

        // ---- Assert ----
        foreach (var table in new[] { "PaymentAddresses", "BlockchainTransactions", "ColdWalletTransfers",
                     "SellerPayoutIssues", "WalletAddressHistory", "SanctionedAddresses" })
        {
            var values = await ColumnAsync($"SELECT Network AS Value FROM [{table}]");
            Assert.NotEmpty(values);
            Assert.All(values, v => Assert.Equal("TRON", v));
        }

        async Task<string?> TxNetwork(Guid id) =>
            (await ColumnAsync($"SELECT Network AS Value FROM Transactions WHERE Id = '{id}'")).Single();

        Assert.Equal("TRON", await TxNetwork(accepted.Id));
        Assert.Equal("TRON", await TxNetwork(completed.Id));
        Assert.Equal("TRON", await TxNetwork(allocatedOnly.Id));
        Assert.Null(await TxNetwork(unaccepted.Id));

        // The rolled-forward schema is the model again: EF reads every row.
        await using var read = CreateContext();
        Assert.Equal(2, await read.Set<PaymentAddress>().CountAsync(p => p.Network == BlockchainNetwork.TRON));
        Assert.Equal(BlockchainNetwork.TRON,
            (await read.Set<SanctionedAddress>().SingleAsync()).Network);
    }

    // ===================== Down guard =====================

    [Fact]
    public async Task Down_WithANonTronRow_IsRefused_AndChangesNothing()
    {
        var seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000000921", SteamDisplayName = "Seller" };
        var tx = NewTransaction(seller.Id, TransactionStatus.ACCEPTED, BlockchainNetwork.ETHEREUM);

        await using (var ctx = CreateContext())
        {
            ctx.Set<User>().Add(seller);
            ctx.Set<Transaction>().Add(tx);
            await ctx.SaveChangesAsync();
            ctx.Set<PaymentAddress>().Add(NewPaymentAddress(tx.Id, BlockchainNetwork.ETHEREUM, EvmLower, 0));
            await ctx.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<SqlException>(() => MigrateToAsync(PreviousMigration()));
        Assert.Equal(50145, ex.Number);

        var applied = await Context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.EndsWith(T145Suffix, StringComparison.Ordinal));
        Assert.Equal("ETHEREUM", Assert.Single(await ColumnAsync("SELECT Network AS Value FROM PaymentAddresses")));
    }
}
