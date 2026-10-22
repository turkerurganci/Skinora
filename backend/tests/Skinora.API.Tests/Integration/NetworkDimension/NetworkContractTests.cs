using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.Webhooks;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;
using static Skinora.API.Tests.Integration.NetworkDimension.NetworkDimensionTestSupport;

namespace Skinora.API.Tests.Integration.NetworkDimension;

/// <summary>
/// T148 — the per-network service contract on the migrated SQL Server schema
/// (05 §3.3a): a webhook whose network differs from the referenced deposit is
/// rejected without writing anything, the confirmation count stored on a row
/// comes from the network's policy (TRON's 20-block floor is not applied to
/// Ethereum, and the database accepts what the policy writes), and the
/// per-network settings read their own rows.
/// </summary>
public class NetworkContractTests : IntegrationTestBase
{
    protected override bool UseMigrations => true;

    // T146 added a Skinora.Shared.Blockchain.TronAddress type; the fixture's
    // constant is aliased so the two names cannot collide.
    private const string TronDeposit = NetworkDimensionTestSupport.TronAddress;

    static NetworkContractTests() => RegisterAllModules();

    private User _seller = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000951",
            SteamDisplayName = "T148 Seller",
        };
        context.Set<User>().Add(_seller);
        await context.SaveChangesAsync();
    }

    private async Task<PaymentAddress> SeedPaymentAddressAsync(
        BlockchainNetwork network, string address, int hdIndex,
        MonitoringStatus status = MonitoringStatus.ACTIVE)
    {
        await using var ctx = CreateContext();
        var tx = NewTransaction(_seller.Id, network: network);
        ctx.Set<Transaction>().Add(tx);
        await ctx.SaveChangesAsync();
        var pa = NewPaymentAddress(tx.Id, network, address, hdIndex);
        pa.MonitoringStatus = status;
        if (status != MonitoringStatus.ACTIVE)
            pa.MonitoringExpiresAt = DateTime.UtcNow.AddDays(1);
        ctx.Set<PaymentAddress>().Add(pa);
        await ctx.SaveChangesAsync();
        return pa;
    }

    private BlockchainWebhookHandler NewHandler(AppDbContext ctx) => new(
        ctx,
        Mock.Of<IAmountValidationService>(),
        new FakeTimeProvider(DateTimeOffset.UtcNow),
        NullLogger<BlockchainWebhookHandler>.Instance);

    private async Task<int> RowsForAsync(Guid paymentAddressId)
    {
        await using var ctx = CreateContext();
        return await ctx.Set<BlockchainTransaction>().CountAsync(b => b.PaymentAddressId == paymentAddressId);
    }

    // ===================== Webhook network must match the deposit =====================

    [Fact]
    public async Task Every_Webhook_Kind_With_The_Wrong_Network_Is_Rejected_And_Writes_Nothing()
    {
        var pa = await SeedPaymentAddressAsync(BlockchainNetwork.TRON, TronDeposit, hdIndex: 70,
            status: MonitoringStatus.POST_CANCEL_24H);
        const string wrong = "ETHEREUM";
        var hash = new string('c', 64);

        await using (var ctx = CreateContext())
        {
            var h = NewHandler(ctx);
            Assert.Equal(BlockchainWebhookResult.NetworkMismatch, await h.HandlePaymentDetectedAsync(
                new()
                {
                    Event = BlockchainWebhookEvents.PaymentDetected,
                    Network = wrong,
                    Data = new()
                    {
                        PaymentAddressId = pa.Id,
                        TransactionId = pa.TransactionId,
                        TxHash = hash,
                        FromAddress = TronDeposit,
                        ToAddress = pa.Address,
                        TokenSymbol = "USDT",
                        Amount = "51.000000",
                    }
                }, "c1", default));
            Assert.Equal(BlockchainWebhookResult.NetworkMismatch, await h.HandlePaymentConfirmedAsync(
                new()
                {
                    Event = BlockchainWebhookEvents.PaymentConfirmed,
                    Network = wrong,
                    Data = new()
                    {
                        PaymentAddressId = pa.Id,
                        TransactionId = pa.TransactionId,
                        TxHash = hash,
                        BlockNumber = 1,
                        ConfirmationCount = 20,
                    }
                }, "c2", default));
            Assert.Equal(BlockchainWebhookResult.NetworkMismatch, await h.HandleWrongTokenIncomingAsync(
                new()
                {
                    Event = BlockchainWebhookEvents.WrongTokenIncoming,
                    Network = wrong,
                    Data = new()
                    {
                        PaymentAddressId = pa.Id,
                        TransactionId = pa.TransactionId,
                        TxHash = hash,
                        FromAddress = TronDeposit,
                        ToAddress = pa.Address,
                        ActualContractAddress = SellerWallet,
                        ActualTokenSymbol = "USDC",
                        Amount = "51.000000",
                    }
                }, "c3", default));
            Assert.Equal(BlockchainWebhookResult.NetworkMismatch, await h.HandleSpamTokenIncomingAsync(
                new()
                {
                    Event = BlockchainWebhookEvents.SpamTokenIncoming,
                    Network = wrong,
                    Data = new()
                    {
                        PaymentAddressId = pa.Id,
                        TransactionId = pa.TransactionId,
                        TxHash = hash,
                        FromAddress = TronDeposit,
                        ToAddress = pa.Address,
                        ActualContractAddress = SellerWallet,
                        Amount = "51.000000",
                    }
                }, "c4", default));
            Assert.Equal(BlockchainWebhookResult.NetworkMismatch, await h.HandleLatePaymentDetectedAsync(
                new()
                {
                    Event = BlockchainWebhookEvents.LatePaymentDetected,
                    Network = wrong,
                    Data = new()
                    {
                        PaymentAddressId = pa.Id,
                        TransactionId = pa.TransactionId,
                        TxHash = hash,
                        FromAddress = TronDeposit,
                        ToAddress = pa.Address,
                        TokenSymbol = "USDT",
                        Amount = "51.000000",
                        MonitorState = "POST_CANCEL_24H",
                    }
                }, "c5", default));
            Assert.Equal(BlockchainWebhookResult.NetworkMismatch, await h.HandlePostCancelMonitorStateChangedAsync(
                new()
                {
                    Event = BlockchainWebhookEvents.PostCancelMonitorStateChanged,
                    Network = wrong,
                    Data = new()
                    {
                        PaymentAddressId = pa.Id,
                        TransactionId = pa.TransactionId,
                        Address = pa.Address,
                        PreviousState = "POST_CANCEL_24H",
                        NewState = "STOPPED",
                    }
                }, "c6", default));
        }

        Assert.Equal(0, await RowsForAsync(pa.Id));
        await using var verify = CreateContext();
        var reloaded = await verify.Set<PaymentAddress>().AsNoTracking().SingleAsync(p => p.Id == pa.Id);
        Assert.Equal(MonitoringStatus.POST_CANCEL_24H, reloaded.MonitoringStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tron")]
    [InlineData("0")]
    [InlineData("BSC")]
    public async Task Webhook_With_A_Missing_Or_Unknown_Network_Is_Invalid(string? network)
    {
        var pa = await SeedPaymentAddressAsync(BlockchainNetwork.TRON, TronDeposit, hdIndex: 71);

        await using (var ctx = CreateContext())
        {
            var result = await NewHandler(ctx).HandlePaymentDetectedAsync(
                new()
                {
                    Event = BlockchainWebhookEvents.PaymentDetected,
                    Network = network,
                    Data = new()
                    {
                        PaymentAddressId = pa.Id,
                        TransactionId = pa.TransactionId,
                        TxHash = new string('d', 64),
                        FromAddress = TronDeposit,
                        ToAddress = pa.Address,
                        TokenSymbol = "USDT",
                        Amount = "51.000000",
                    }
                }, "c", default);
            Assert.Equal(BlockchainWebhookResult.Invalid, result);
        }

        Assert.Equal(0, await RowsForAsync(pa.Id));
    }

    // ===================== Confirmation count comes from the network policy =====================

    [Fact]
    public async Task Tron_Confirmation_Is_Stored_At_Its_20_Block_Floor_Ethereum_At_What_Was_Reported()
    {
        var tronPa = await SeedPaymentAddressAsync(BlockchainNetwork.TRON, TronDeposit, hdIndex: 72);
        var ethPa = await SeedPaymentAddressAsync(BlockchainNetwork.ETHEREUM, EvmLower, hdIndex: 72);
        var tronHash = new string('e', 64);
        var ethHash = "0x" + new string('e', 64);

        async Task DetectAndConfirmAsync(PaymentAddress pa, string hash, int reportedConfirmations)
        {
            await using var ctx = CreateContext();
            var h = NewHandler(ctx);
            var network = pa.Network.ToString();
            Assert.Equal(BlockchainWebhookResult.Applied, await h.HandlePaymentDetectedAsync(
                new()
                {
                    Event = BlockchainWebhookEvents.PaymentDetected,
                    Network = network,
                    Data = new()
                    {
                        PaymentAddressId = pa.Id,
                        TransactionId = pa.TransactionId,
                        TxHash = hash,
                        FromAddress = pa.Network == BlockchainNetwork.TRON ? SellerWallet : EvmLower2,
                        ToAddress = pa.Address,
                        TokenSymbol = "USDT",
                        Amount = "51.000000",
                    }
                }, "d", default));
            Assert.Equal(BlockchainWebhookResult.Applied, await h.HandlePaymentConfirmedAsync(
                new()
                {
                    Event = BlockchainWebhookEvents.PaymentConfirmed,
                    Network = network,
                    Data = new()
                    {
                        PaymentAddressId = pa.Id,
                        TransactionId = pa.TransactionId,
                        TxHash = hash,
                        BlockNumber = 100,
                        ConfirmationCount = reportedConfirmations,
                    }
                }, "c", default));
        }

        // TRON reports 5 for a final transfer → stored at the 20 floor (the
        // CONFIRMED CHECK requires ≥ 20 on TRON). Ethereum reports 3 → stored
        // as 3: the TRON floor is not applied, and the database accepts it.
        await DetectAndConfirmAsync(tronPa, tronHash, 5);
        await DetectAndConfirmAsync(ethPa, ethHash, 3);

        await using var verify = CreateContext();
        var tronRow = await verify.Set<BlockchainTransaction>().AsNoTracking().SingleAsync(b => b.PaymentAddressId == tronPa.Id);
        var ethRow = await verify.Set<BlockchainTransaction>().AsNoTracking().SingleAsync(b => b.PaymentAddressId == ethPa.Id);
        Assert.Equal((BlockchainTransactionStatus.CONFIRMED, 20), (tronRow.Status, tronRow.ConfirmationCount));
        Assert.Equal((BlockchainTransactionStatus.CONFIRMED, 3), (ethRow.Status, ethRow.ConfirmationCount));
        Assert.Equal(BlockchainNetwork.ETHEREUM, ethRow.Network);
    }

    [Fact]
    public async Task Spam_Token_On_Ethereum_Is_Recorded_Without_Trons_Synthetic_20()
    {
        var ethPa = await SeedPaymentAddressAsync(BlockchainNetwork.ETHEREUM, EvmLower, hdIndex: 73);

        await using (var ctx = CreateContext())
        {
            Assert.Equal(BlockchainWebhookResult.Applied, await NewHandler(ctx).HandleSpamTokenIncomingAsync(
                new()
                {
                    Event = BlockchainWebhookEvents.SpamTokenIncoming,
                    Network = "ETHEREUM",
                    Data = new()
                    {
                        PaymentAddressId = ethPa.Id,
                        TransactionId = ethPa.TransactionId,
                        TxHash = "0x" + new string('f', 64),
                        FromAddress = EvmLower2,
                        ToAddress = ethPa.Address,
                        ActualContractAddress = EvmLower2,
                        Amount = "1000.000000",
                    }
                }, "s", default));
        }

        await using var verify = CreateContext();
        var row = await verify.Set<BlockchainTransaction>().AsNoTracking().SingleAsync(b => b.PaymentAddressId == ethPa.Id);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, row.Status);
        Assert.Equal(0, row.ConfirmationCount);
    }

    // ===================== Per-network settings read their own rows =====================

    [Fact]
    public async Task Network_Settings_Read_The_Seeded_Rows_Tron_Equals_Today_Ethereum_Equals_T142()
    {
        await using var ctx = CreateContext();
        var provider = new BlockchainNetworkSettingsProvider(ctx);

        var tron = await provider.GetAsync(BlockchainNetwork.TRON, default);
        var ethereum = await provider.GetAsync(BlockchainNetwork.ETHEREUM, default);

        // TRON — the values the platform ran with before T148, moved by T169
        // (refund 2.0 → 2.60, payout 0.50 → 2.25, TRX floor 100 → 1500) and
        // its own minimum price (50); T169a moved the refund 2.60 → 2.80 (the
        // refund fee is now the platform's whole outlay) and added the deposit
        // refund daily limit (100 per network).
        Assert.Equal(2.80m, tron.RefundGasFeeFallbackUsdt);
        Assert.Equal(100m, tron.DepositRefundDailyLimitUsdt);
        Assert.Equal(100m, ethereum.DepositRefundDailyLimitUsdt);
        Assert.Equal(2.25m, tron.PayoutGasFeeFallbackUsdt);
        Assert.Equal(10.0m, tron.MaxChargedGasFeeUsdt);
        Assert.Equal(1500m, tron.HotWalletNativeBalanceMinimum);
        Assert.Equal(50m, tron.MinTransactionAmountUsdt);
        Assert.Null(tron.FeeCapUsdPerTransfer);
        Assert.Null(tron.FinalityStallAlarmMinutes);

        // Ethereum — T142's measured proposals (refund fallback 0.50 → 0.20, T169).
        Assert.Equal(0.20m, ethereum.RefundGasFeeFallbackUsdt);
        Assert.Equal(0.50m, ethereum.PayoutGasFeeFallbackUsdt);
        Assert.Equal(10.0m, ethereum.MaxChargedGasFeeUsdt);
        Assert.Equal(0.06m, ethereum.HotWalletNativeBalanceMinimum);
        Assert.Equal(0.06m, ethereum.FuelWalletNativeBalanceMinimum);
        Assert.Equal(0.50m, ethereum.FeeCapUsdPerTransfer);
        Assert.Equal(180, ethereum.FeeCapWaitAlarmMinutes);
        Assert.Equal(30, ethereum.FinalityStallAlarmMinutes);
        Assert.Null(ethereum.MinTransactionAmountUsdt);
    }

    [Fact]
    public async Task An_Admin_Change_To_One_Network_Does_Not_Move_The_Other()
    {
        await using (var ctx = CreateContext())
        {
            var row = await ctx.Set<SystemSetting>()
                .SingleAsync(s => s.Key == BlockchainNetworkSettingKeys.Ethereum.RefundGasFeeFallbackUsdt);
            row.Value = "0.80";
            await ctx.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var provider = new BlockchainNetworkSettingsProvider(read);
        Assert.Equal(0.80m, (await provider.GetAsync(BlockchainNetwork.ETHEREUM, default)).RefundGasFeeFallbackUsdt);
        Assert.Equal(2.80m, (await provider.GetAsync(BlockchainNetwork.TRON, default)).RefundGasFeeFallbackUsdt);
    }
}

/// <summary>
/// T148 — rehearsal of <c>T148_NetworkSettings</c> on real SQL Server: on a
/// clean database every Ethereum row arrives with its T142 value and TRON's
/// rows are the pre-T148 ones; on a database with existing rows an admin-tuned
/// TRON value survives the migration untouched; Down removes exactly the
/// Ethereum rows.
/// </summary>
public class NetworkSettingsMigrationRehearsalTests : IntegrationTestBase
{
    private const string T148Suffix = "_T148_NetworkSettings";

    protected override bool UseMigrations => true;

    static NetworkSettingsMigrationRehearsalTests() => RegisterAllModules();

    private string MigrationBeforeT148()
    {
        var all = Context.Database.GetMigrations().ToList();
        var index = all.FindIndex(m => m.EndsWith(T148Suffix, StringComparison.Ordinal));
        Assert.True(index > 0, "T148 migration not found in the assembly.");
        return all[index - 1];
    }

    private async Task MigrateToAsync(string? target)
    {
        await using var ctx = CreateContext();
        await ctx.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task<Dictionary<string, string?>> SettingsAsync()
    {
        await using var ctx = CreateContext();
        return await ctx.Set<SystemSetting>().AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value);
    }

    [Fact]
    public async Task CleanDatabase_Has_Every_Seeded_Key_With_Ethereum_At_T142_Values()
    {
        var applied = await Context.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.EndsWith(T148Suffix, StringComparison.Ordinal));

        var settings = await SettingsAsync();
        Assert.Equal(SystemSettingSeed.All.Count, settings.Count);
        foreach (var seed in SystemSettingSeed.All)
            Assert.Equal(seed.Value, settings[seed.Key]);

        Assert.Equal("0.50", settings["blockchain.ethereum.fee_cap_usd_per_transfer"]);
        Assert.Equal("180", settings["blockchain.ethereum.fee_cap_wait_alarm_minutes"]);
        Assert.Equal("0.06", settings["blockchain.ethereum.fuel_wallet_eth_balance_minimum"]);
        Assert.Equal("2.80", settings["blockchain.refund_gas_fee_estimate_usdt"]); // T169 2.60 → T169a 2.80
        Assert.Equal("100", settings["blockchain.tron.deposit_refund_daily_limit_usdt"]); // T169a
        Assert.Equal("100", settings["blockchain.ethereum.deposit_refund_daily_limit_usdt"]); // T169a
    }

    [Fact]
    public async Task ExistingRows_TunedTronValueSurvives_EthereumRowsAreAdded_AndDownRemovesOnlyThem()
    {
        var before = MigrationBeforeT148();
        await MigrateToAsync(before);

        // The pre-T148 database: an admin has tuned TRON's refund fallback and
        // TRX floor; no Ethereum row exists.
        await using (var ctx = CreateContext())
        {
            var refund = await ctx.Set<SystemSetting>().SingleAsync(s => s.Key == "blockchain.refund_gas_fee_estimate_usdt");
            refund.Value = "3.25";
            var trx = await ctx.Set<SystemSetting>().SingleAsync(s => s.Key == "hot_wallet.trx_balance_minimum");
            trx.Value = "250";
            await ctx.SaveChangesAsync();
            Assert.False(await ctx.Set<SystemSetting>().AnyAsync(s => s.Key.StartsWith("blockchain.ethereum.")));
        }

        await MigrateToAsync(null);

        var after = await SettingsAsync();
        Assert.Equal("3.25", after["blockchain.refund_gas_fee_estimate_usdt"]);
        Assert.Equal("250", after["hot_wallet.trx_balance_minimum"]);
        var ethereumKeys = BlockchainNetworkSettingKeys.Ethereum.AllKeys();
        // T148's eight rows + T149's minimum-refund multiplier; migrating to
        // the latest applies both, and rolling back to before T148 removes both.
        Assert.Equal(9, ethereumKeys.Count);
        foreach (var key in ethereumKeys)
            Assert.Equal(SystemSettingSeed.All.Single(s => s.Key == key).Value, after[key]);

        await MigrateToAsync(before);

        var rolledBack = await SettingsAsync();
        Assert.DoesNotContain(rolledBack.Keys, k => k.StartsWith("blockchain.ethereum.", StringComparison.Ordinal));
        Assert.Equal("3.25", rolledBack["blockchain.refund_gas_fee_estimate_usdt"]);

        // Leave the database where the base class expects it.
        await MigrateToAsync(null);
    }
}
