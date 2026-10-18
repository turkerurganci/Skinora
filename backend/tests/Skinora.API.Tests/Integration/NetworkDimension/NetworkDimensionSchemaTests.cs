using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Skinora.Payments.Domain.Entities;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Webhooks;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;
using static Skinora.API.Tests.Integration.NetworkDimension.NetworkDimensionTestSupport;

namespace Skinora.API.Tests.Integration.NetworkDimension;

/// <summary>
/// T145 — network dimension on the migrated SQL Server schema (06 §1.4):
/// network-scoped uniqueness keys, network-aware confirmation CHECKs, the
/// sanctions network CHECK, the single canonical stored form of on-chain
/// identifiers, and the two code paths whose key now includes the network
/// (sanctions lookup, webhook de-duplication).
/// </summary>
/// <remarks>
/// Runs on <c>MigrateAsync</c>, not <c>EnsureCreated</c>, so every assertion is
/// against the schema the migration actually produces.
/// </remarks>
public class NetworkDimensionSchemaTests : IntegrationTestBase
{
    protected override bool UseMigrations => true;

    static NetworkDimensionSchemaTests() => RegisterAllModules();

    private User _seller = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000901",
            SteamDisplayName = "T145 Seller",
        };
        context.Set<User>().Add(_seller);
        await context.SaveChangesAsync();
    }

    private async Task<Transaction> SeedTransactionAsync(BlockchainNetwork? network = null)
    {
        await using var ctx = CreateContext();
        var tx = NewTransaction(_seller.Id, network: network);
        ctx.Set<Transaction>().Add(tx);
        await ctx.SaveChangesAsync();
        return tx;
    }

    private async Task<PaymentAddress> SeedPaymentAddressAsync(
        BlockchainNetwork network, string address, int hdIndex)
    {
        var tx = await SeedTransactionAsync(network);
        await using var ctx = CreateContext();
        var pa = NewPaymentAddress(tx.Id, network, address, hdIndex);
        ctx.Set<PaymentAddress>().Add(pa);
        await ctx.SaveChangesAsync();
        return pa;
    }

    private async Task<string?> ScalarAsync(string sql)
    {
        await using var ctx = CreateContext();
        return await ctx.Database.SqlQueryRaw<string>(sql).SingleOrDefaultAsync();
    }

    // ===================== Uniqueness keys include the network =====================

    [Fact]
    public async Task PaymentAddress_SameAddress_IsUniquePerNetwork()
    {
        await SeedPaymentAddressAsync(BlockchainNetwork.ETHEREUM, EvmLower, hdIndex: 1);

        // Same string on another network: a different (Network, Address) key.
        await SeedPaymentAddressAsync(BlockchainNetwork.TRON, EvmLower, hdIndex: 1);

        // Same network again: rejected.
        await Assert.ThrowsAsync<DbUpdateException>(
            () => SeedPaymentAddressAsync(BlockchainNetwork.ETHEREUM, EvmLower, hdIndex: 2));
    }

    [Fact]
    public async Task PaymentAddress_HdWalletIndex_IsUniquePerNetwork()
    {
        await SeedPaymentAddressAsync(BlockchainNetwork.TRON, TronAddress, hdIndex: 7);

        // Each network has its own seed (11 §F8 K7): index 7 exists once per network.
        await SeedPaymentAddressAsync(BlockchainNetwork.ETHEREUM, EvmLower, hdIndex: 7);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => SeedPaymentAddressAsync(BlockchainNetwork.TRON, SellerWallet, hdIndex: 7));
    }

    [Fact]
    public async Task PaymentAddress_EvmAddressInOtherLetterCase_IsTheSameKey()
    {
        // The canonical converter stores both spellings as one lowercase value,
        // so the UQ sees the duplicate regardless of the database collation.
        await SeedPaymentAddressAsync(BlockchainNetwork.ETHEREUM, EvmChecksummed, hdIndex: 3);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => SeedPaymentAddressAsync(BlockchainNetwork.ETHEREUM, EvmLower.ToUpperInvariant().Replace("0X", "0x"), hdIndex: 4));
    }

    [Fact]
    public async Task BlockchainTransaction_TxHashEventIndex_IsUniquePerNetwork()
    {
        const string hash = "0x9f1c0e8a1b2c3d4e5f60718293a4b5c6d7e8f90112233445566778899aabbccd";
        var tronPa = await SeedPaymentAddressAsync(BlockchainNetwork.TRON, TronAddress, hdIndex: 0);
        var ethPa = await SeedPaymentAddressAsync(BlockchainNetwork.ETHEREUM, EvmLower, hdIndex: 0);

        await using (var ctx = CreateContext())
        {
            ctx.Set<BlockchainTransaction>().Add(
                NewInbound(tronPa.TransactionId, tronPa.Id, BlockchainNetwork.TRON, hash, 0));
            ctx.Set<BlockchainTransaction>().Add(
                NewInbound(ethPa.TransactionId, ethPa.Id, BlockchainNetwork.ETHEREUM, hash, 0));
            await ctx.SaveChangesAsync();
        }

        await using var dup = CreateContext();
        dup.Set<BlockchainTransaction>().Add(
            NewInbound(ethPa.TransactionId, ethPa.Id, BlockchainNetwork.ETHEREUM, hash.ToUpperInvariant().Replace("0X", "0x"), 0));
        await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());
    }

    [Fact]
    public async Task ColdWalletTransfer_TxHash_IsUniquePerNetwork()
    {
        var hash = string.Concat(Enumerable.Repeat("abc123de", 8)); // TRON-shaped, synthetic

        ColdWalletTransfer Transfer(BlockchainNetwork network) => new()
        {
            Network = network,
            Amount = 10m,
            Token = StablecoinType.USDT,
            FromAddress = TronAddress,
            ToAddress = SellerWallet,
            TxHash = hash,
            InitiatedByAdminId = _seller.Id,
            CreatedAt = DateTime.UtcNow,
        };

        await using (var ctx = CreateContext())
        {
            ctx.Set<ColdWalletTransfer>().Add(Transfer(BlockchainNetwork.TRON));
            ctx.Set<ColdWalletTransfer>().Add(Transfer(BlockchainNetwork.ETHEREUM));
            await ctx.SaveChangesAsync();
        }

        await using var dup = CreateContext();
        dup.Set<ColdWalletTransfer>().Add(Transfer(BlockchainNetwork.TRON));
        await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());
    }

    [Fact]
    public async Task SanctionedAddress_ActiveAddress_IsUniquePerNetwork()
    {
        SanctionedAddress Row(BlockchainNetwork network, string address) => new()
        {
            Id = Guid.NewGuid(),
            Address = address,
            Network = network,
            Source = SanctionedAddressSources.Manual,
            ListedAt = DateTime.UtcNow,
            AddedByAdminId = _seller.Id,
            IsActive = true,
        };

        await using (var ctx = CreateContext())
        {
            ctx.Set<SanctionedAddress>().Add(Row(BlockchainNetwork.ETHEREUM, EvmLower));
            ctx.Set<SanctionedAddress>().Add(Row(BlockchainNetwork.TRON, EvmLower));
            await ctx.SaveChangesAsync();
        }

        await using var dup = CreateContext();
        dup.Set<SanctionedAddress>().Add(Row(BlockchainNetwork.ETHEREUM, EvmChecksummed));
        await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());
    }

    // ===================== CHECK constraints =====================

    [Fact]
    public async Task SanctionedAddress_NetworkCheck_AcceptsEnumNames_RejectsPreF8Value()
    {
        await using var ctx = CreateContext();
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // 'ETHEREUM' is accepted by the widened CHECK.
        await ctx.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO SanctionedAddresses (Id, Address, Network, Source, ListedAt, IsActive, CreatedAt, UpdatedAt)
            VALUES ({id}, {EvmLower2}, 'ETHEREUM', 'MANUAL', {now}, 1, {now}, {now})");

        // The pre-F8 value no longer is.
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE SanctionedAddresses SET Network = 'TRC-20' WHERE Id = {id}"));
    }

    [Fact]
    public async Task ConfirmedCheck_TwentyBlocksIsTronOnly()
    {
        var tronPa = await SeedPaymentAddressAsync(BlockchainNetwork.TRON, TronAddress, hdIndex: 20);
        var ethPa = await SeedPaymentAddressAsync(BlockchainNetwork.ETHEREUM, EvmLower, hdIndex: 20);

        // Ethereum confirms on `finalized`, not on a block count (08 §3.7.4).
        await using (var ok = CreateContext())
        {
            var eth = NewInbound(ethPa.TransactionId, ethPa.Id, BlockchainNetwork.ETHEREUM,
                "0x" + new string('1', 64));
            eth.Status = BlockchainTransactionStatus.CONFIRMED;
            eth.ConfirmationCount = 0;
            eth.ConfirmedAt = DateTime.UtcNow;
            ok.Set<BlockchainTransaction>().Add(eth);
            await ok.SaveChangesAsync();
        }

        // TRON keeps the 20-block rule.
        await using var bad = CreateContext();
        var tron = NewInbound(tronPa.TransactionId, tronPa.Id, BlockchainNetwork.TRON, new string('2', 64));
        tron.Status = BlockchainTransactionStatus.CONFIRMED;
        tron.ConfirmationCount = 5;
        tron.ConfirmedAt = DateTime.UtcNow;
        bad.Set<BlockchainTransaction>().Add(tron);
        await Assert.ThrowsAsync<DbUpdateException>(() => bad.SaveChangesAsync());
    }

    [Fact]
    public async Task PendingCheck_TwentyBlockCeilingIsTronOnly()
    {
        var tronPa = await SeedPaymentAddressAsync(BlockchainNetwork.TRON, TronAddress, hdIndex: 21);
        var ethPa = await SeedPaymentAddressAsync(BlockchainNetwork.ETHEREUM, EvmLower, hdIndex: 21);

        // An Ethereum transfer waits ~64–96 blocks for `finalized` while PENDING.
        await using (var ok = CreateContext())
        {
            var eth = NewInbound(ethPa.TransactionId, ethPa.Id, BlockchainNetwork.ETHEREUM,
                "0x" + new string('3', 64));
            eth.Status = BlockchainTransactionStatus.PENDING;
            eth.ConfirmationCount = 40;
            ok.Set<BlockchainTransaction>().Add(eth);
            await ok.SaveChangesAsync();
        }

        await using var bad = CreateContext();
        var tron = NewInbound(tronPa.TransactionId, tronPa.Id, BlockchainNetwork.TRON, new string('4', 64));
        tron.Status = BlockchainTransactionStatus.PENDING;
        tron.ConfirmationCount = 40;
        bad.Set<BlockchainTransaction>().Add(tron);
        await Assert.ThrowsAsync<DbUpdateException>(() => bad.SaveChangesAsync());
    }

    // ===================== Canonical stored form =====================

    [Fact]
    public async Task EvmIdentifiers_AreStoredLowercase_TronValuesUnchanged()
    {
        var ethPa = await SeedPaymentAddressAsync(BlockchainNetwork.ETHEREUM, EvmChecksummed, hdIndex: 30);
        var tronPa = await SeedPaymentAddressAsync(BlockchainNetwork.TRON, TronAddress, hdIndex: 30);
        var hashMixed = "0xAbCdEf" + new string('0', 58);

        await using (var ctx = CreateContext())
        {
            var row = NewInbound(ethPa.TransactionId, ethPa.Id, BlockchainNetwork.ETHEREUM, hashMixed);
            row.Type = BlockchainTransactionType.WRONG_TOKEN_INCOMING;
            row.ActualTokenAddress = "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48";
            ctx.Set<BlockchainTransaction>().Add(row);

            var user = new User
            {
                Id = Guid.NewGuid(),
                SteamId = "76561198000000903",
                SteamDisplayName = "Canonical",
                WalletAddresses =
                {
                    UserWalletAddress.For(BlockchainNetwork.ETHEREUM, WalletAddressType.PAYOUT, EvmChecksummed2),
                    UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.REFUND, TronAddress),
                },
            };
            ctx.Set<User>().Add(user);
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(EvmLower, await ScalarAsync(
            $"SELECT Address AS Value FROM PaymentAddresses WHERE Id = '{ethPa.Id}'"));
        Assert.Equal(TronAddress, await ScalarAsync(
            $"SELECT Address AS Value FROM PaymentAddresses WHERE Id = '{tronPa.Id}'"));
        Assert.Equal(hashMixed.ToLowerInvariant(), await ScalarAsync(
            $"SELECT TxHash AS Value FROM BlockchainTransactions WHERE PaymentAddressId = '{ethPa.Id}'"));
        Assert.Equal(EvmLower2, await ScalarAsync(
            $"SELECT FromAddress AS Value FROM BlockchainTransactions WHERE PaymentAddressId = '{ethPa.Id}'"));
        Assert.Equal(EvmLower, await ScalarAsync(
            $"SELECT ToAddress AS Value FROM BlockchainTransactions WHERE PaymentAddressId = '{ethPa.Id}'"));
        Assert.Equal("0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48", await ScalarAsync(
            $"SELECT ActualTokenAddress AS Value FROM BlockchainTransactions WHERE PaymentAddressId = '{ethPa.Id}'"));
        // T146 — user addresses live in UserWalletAddresses (06 §1.4).
        Assert.Equal(EvmLower2, await ScalarAsync(
            "SELECT w.Address AS Value FROM UserWalletAddresses w JOIN Users u ON u.Id = w.UserId " +
            "WHERE u.SteamId = '76561198000000903' AND w.Type = 'PAYOUT'"));
        Assert.Equal(TronAddress, await ScalarAsync(
            "SELECT w.Address AS Value FROM UserWalletAddresses w JOIN Users u ON u.Id = w.UserId " +
            "WHERE u.SteamId = '76561198000000903' AND w.Type = 'REFUND'"));
    }

    [Fact]
    public async Task EvmQueryParameter_IsCanonicalised_ByTheColumnConverter()
    {
        await SeedPaymentAddressAsync(BlockchainNetwork.ETHEREUM, EvmLower, hdIndex: 31);

        await using var ctx = CreateContext();
        var found = await ctx.Set<PaymentAddress>()
            .AnyAsync(p => p.Network == BlockchainNetwork.ETHEREUM && p.Address == EvmChecksummed);

        Assert.True(found);
    }

    [Fact]
    public async Task NetworkColumns_HaveNoDatabaseDefault()
    {
        // A row inserted without a network must fail, not silently become TRON.
        var count = await ScalarAsync(@"
            SELECT CAST(COUNT(*) AS nvarchar(10)) AS Value
            FROM sys.default_constraints d
            JOIN sys.columns c ON c.object_id = d.parent_object_id AND c.column_id = d.parent_column_id
            WHERE c.name = 'Network'");
        Assert.Equal("0", count);
    }

    // ===================== Sanctions lookup (Network, canonical Address) =====================

    [Fact]
    public async Task SanctionsLookup_MatchesEvmInAnyCase_TronExactly_AndOnlyOnItsNetwork()
    {
        await using (var ctx = CreateContext())
        {
            ctx.Set<SanctionedAddress>().AddRange(
                new SanctionedAddress
                {
                    Id = Guid.NewGuid(),
                    Address = EvmChecksummed,
                    Network = BlockchainNetwork.ETHEREUM,
                    Source = SanctionedAddressSources.Manual,
                    ListedAt = DateTime.UtcNow,
                    IsActive = true,
                },
                new SanctionedAddress
                {
                    Id = Guid.NewGuid(),
                    Address = TronAddress,
                    Network = BlockchainNetwork.TRON,
                    Source = SanctionedAddressSources.Manual,
                    ListedAt = DateTime.UtcNow,
                    IsActive = true,
                });
            await ctx.SaveChangesAsync();
        }

        await using var lookupCtx = CreateContext();
        var lookup = new SanctionedAddressLookup(lookupCtx);

        Assert.NotNull(await lookup.FindActiveAsync(BlockchainNetwork.ETHEREUM, EvmLower, default));
        Assert.NotNull(await lookup.FindActiveAsync(BlockchainNetwork.ETHEREUM, EvmLower.ToUpperInvariant().Replace("0X", "0x"), default));
        Assert.NotNull(await lookup.FindActiveAsync(BlockchainNetwork.TRON, TronAddress, default));

        // Base58 is case-significant: the lowercased TRON string is another
        // address, even though SQL Server's default collation would call it equal.
        Assert.Null(await lookup.FindActiveAsync(BlockchainNetwork.TRON, TronAddress.ToLowerInvariant(), default));

        // The key includes the network.
        Assert.Null(await lookup.FindActiveAsync(BlockchainNetwork.TRON, EvmLower, default));
        Assert.Null(await lookup.FindActiveAsync(BlockchainNetwork.ETHEREUM, TronAddress, default));
    }

    // ===================== Webhook de-duplication key includes the network =====================

    [Fact]
    public async Task WebhookDedup_SameTxHashAndEventIndex_OnAnotherNetwork_IsANewEvent()
    {
        var hash = "0x" + string.Concat(Enumerable.Repeat("7a", 32));
        var tronPa = await SeedPaymentAddressAsync(BlockchainNetwork.TRON, TronAddress, hdIndex: 40);
        var ethPa = await SeedPaymentAddressAsync(BlockchainNetwork.ETHEREUM, EvmLower, hdIndex: 40);

        BlockchainWebhookEnvelope<PaymentDetectedData> Envelope(PaymentAddress pa) => new()
        {
            Event = "payment-detected",
            Data = new PaymentDetectedData
            {
                PaymentAddressId = pa.Id,
                TransactionId = pa.TransactionId,
                TxHash = hash,
                EventIndex = 0,
                FromAddress = pa.Network == BlockchainNetwork.TRON ? SellerWallet : EvmChecksummed2,
                ToAddress = pa.Address,
                TokenSymbol = "USDT",
                Amount = "51.000000",
            },
        };

        async Task<BlockchainWebhookResult> SendAsync(PaymentAddress pa)
        {
            await using var ctx = CreateContext();
            var handler = new BlockchainWebhookHandler(
                ctx,
                Mock.Of<IAmountValidationService>(),
                new FakeTimeProvider(DateTimeOffset.UtcNow),
                NullLogger<BlockchainWebhookHandler>.Instance);
            return await handler.HandlePaymentDetectedAsync(Envelope(pa), "corr-t145", default);
        }

        Assert.Equal(BlockchainWebhookResult.Applied, await SendAsync(tronPa));
        Assert.Equal(BlockchainWebhookResult.Applied, await SendAsync(ethPa));
        Assert.Equal(BlockchainWebhookResult.Idempotent, await SendAsync(tronPa));
        Assert.Equal(BlockchainWebhookResult.Idempotent, await SendAsync(ethPa));

        await using var verify = CreateContext();
        var rows = await verify.Set<BlockchainTransaction>()
            .Where(b => b.TxHash == hash)
            .Select(b => new { b.Network, b.PaymentAddressId })
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Network == BlockchainNetwork.TRON && r.PaymentAddressId == tronPa.Id);
        Assert.Contains(rows, r => r.Network == BlockchainNetwork.ETHEREUM && r.PaymentAddressId == ethPa.Id);
    }
}
