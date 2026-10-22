using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.API.Outbox;
using Skinora.API.Services.AdminWallets;
using Skinora.API.Tests.Common;
using Skinora.Payments.Domain.Entities;
using Skinora.Platform.Application.Wallets;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;
using static Skinora.API.Tests.Integration.NetworkDimension.NetworkDimensionTestSupport;

namespace Skinora.API.Tests.Integration.NetworkDimension;

/// <summary>
/// T157 — AD38 <c>GET /admin/wallets</c>: wallets, balances, the fuel wallet and
/// the transfers of each network, read from that network's own service
/// (07 §9.38). A network's block never carries another network's number.
/// </summary>
public class AdminWalletOverviewTests : IntegrationTestBase
{
    protected override bool UseMigrations => true;

    static AdminWalletOverviewTests() => RegisterAllModules();

    private const string TronHot = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL";
    private const string TronCold = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";
    private const string EthHotLower = "0xdac17f958d2ee523a2206206994597c13d831ec7";
    private const string EthHotDisplay = "0xdAC17F958D2ee523a2206206994597C13D831ec7";
    private const string EthColdLower = "0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed";
    private const string EthFuelDisplay = "0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359";

    private User _admin = null!;
    private User _seller = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _admin = new User { Id = Guid.NewGuid(), SteamId = "76561198000001201", SteamDisplayName = "T157 Wallet Admin" };
        _seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000001202", SteamDisplayName = "T157 Wallet Seller" };
        context.Set<User>().AddRange(_admin, _seller);
        await context.SaveChangesAsync();
    }

    private StubWalletProvider Wallets() => new()
    {
        HotWalletAddress = TronHot,
        ColdWalletAddress = TronCold,
        Pairs =
        {
            [BlockchainNetwork.ETHEREUM] =
                new PlatformWalletAddresses(BlockchainNetwork.ETHEREUM, EthHotLower, EthColdLower),
        },
    };

    private AdminWalletOverviewService Service(
        AppDbContext ctx, FakeBlockchainServiceRegistry registry, StubWalletProvider? wallets = null) =>
        new(ctx, registry, AlwaysHealthyBlockchainNetworks.Instance, wallets ?? Wallets(),
            new BlockchainNetworkSettingsProvider(ctx), NullLogger<AdminWalletOverviewService>.Instance);

    private static OverviewSidecar TronSidecar() => new()
    {
        Balances = new BlockchainSidecarBalancesResult(BlockchainSidecarStatus.Success, 7_000_001,
        [
            new(TronHot, new Dictionary<string, string> { ["TRX"] = "2500000000", ["USDT"] = "1500250000", ["USDC"] = "0" }),
            new(TronCold, new Dictionary<string, string> { ["TRX"] = "0", ["USDT"] = "90000000", ["USDC"] = "0" }),
        ]),
    };

    private static OverviewSidecar EthereumSidecar(string fuelAddress = EthFuelDisplay, string hot = EthHotDisplay) => new()
    {
        Platform = new BlockchainSidecarPlatformWalletsResult(
            BlockchainSidecarStatus.Success, hot, fuelAddress, "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed"),
        Balances = new BlockchainSidecarBalancesResult(BlockchainSidecarStatus.Success, 9_000_002,
        [
            // 0.123456789012345678 ETH, 2,000.5 USDT
            new(EthHotLower, new Dictionary<string, string>
                { ["ETH"] = "123456789012345678", ["USDT"] = "2000500000", ["USDC"] = "0" }),
            new(EthColdLower, new Dictionary<string, string>
                { ["ETH"] = "0", ["USDT"] = "7000000", ["USDC"] = "0" }),
            // 0.05 ETH — below the 0.06 floor
            new(EthFuelDisplay.ToLowerInvariant(), new Dictionary<string, string>
                { ["ETH"] = "50000000000000000", ["USDT"] = "0", ["USDC"] = "0" }),
        ]),
    };

    private static NetworkWalletsDto Block(WalletOverviewDto overview, BlockchainNetwork network) =>
        overview.Networks.Single(n => n.Network == network);

    private static string Balance(PlatformWalletDto wallet, string symbol) =>
        wallet.Balances!.Single(b => b.Symbol == symbol).Amount;

    [Fact]
    public async Task EachNetworkShowsItsOwnWallets_Balances_AndTheEthereumFuelWallet()
    {
        var tron = TronSidecar();
        var ethereum = EthereumSidecar();
        var registry = new FakeBlockchainServiceRegistry()
            .With(BlockchainNetwork.TRON, sidecar: tron)
            .With(BlockchainNetwork.ETHEREUM, sidecar: ethereum);

        await using var ctx = CreateContext();
        var overview = await Service(ctx, registry).GetAsync(CancellationToken.None);

        // TRON: hot + cold, no fuel wallet; TRX in SUN scaled by 6, stablecoins by 6.
        var t = Block(overview, BlockchainNetwork.TRON);
        Assert.Equal("TRX", t.NativeAsset);
        Assert.Equal("OK", t.BalancesStatus);
        Assert.Equal(7_000_001, t.BalancesBlock);
        Assert.Equal(["HOT", "COLD"], t.Wallets.Select(w => w.Role).ToArray());
        var tHot = t.Wallets[0];
        Assert.Equal(TronHot, tHot.Address);
        Assert.Equal("2500", Balance(tHot, "TRX"));
        Assert.Equal("1500.25", Balance(tHot, "USDT"));
        Assert.Equal("1500", tHot.NativeFloor); // 1500 TRX seeded floor (T169)
        Assert.False(tHot.BelowNativeFloor);

        // Ethereum: hot + cold + fuel (the address comes from the service), ETH scaled by 18.
        var e = Block(overview, BlockchainNetwork.ETHEREUM);
        Assert.Equal("ETH", e.NativeAsset);
        Assert.Equal(["HOT", "COLD", "FUEL"], e.Wallets.Select(w => w.Role).ToArray());
        var eHot = e.Wallets[0];
        Assert.Equal(EthHotDisplay, eHot.Address); // EIP-55 display form
        Assert.Equal("0.123456789012345678", Balance(eHot, "ETH"));
        Assert.Equal("2000.5", Balance(eHot, "USDT"));
        Assert.False(eHot.AddressMismatch);
        Assert.False(eHot.BelowNativeFloor);

        var fuel = e.Wallets[2];
        Assert.Equal(EthFuelDisplay, fuel.Address);
        Assert.Equal("0.05", Balance(fuel, "ETH"));
        Assert.Equal("0.06", fuel.NativeFloor);
        Assert.True(fuel.BelowNativeFloor);

        // Each service was asked only about its own addresses.
        Assert.DoesNotContain(EthHotLower, tron.QueriedAddresses, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(TronHot, ethereum.QueriedAddresses);
        Assert.Contains(EthFuelDisplay, ethereum.QueriedAddresses, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnconfiguredNetwork_IsShownAsSuch_AndItsServiceIsNeverAsked()
    {
        var tron = TronSidecar();
        var registry = new FakeBlockchainServiceRegistry().With(BlockchainNetwork.TRON, sidecar: tron);

        await using var ctx = CreateContext();
        var overview = await Service(ctx, registry).GetAsync(CancellationToken.None);

        var e = Block(overview, BlockchainNetwork.ETHEREUM);
        Assert.False(e.ServiceConfigured);
        Assert.Equal("NOT_CONFIGURED", e.BalancesStatus);
        Assert.All(e.Wallets, w => Assert.Null(w.Balances));
        Assert.DoesNotContain(registry.Requests, r => r is { Kind: "Sidecar", Network: BlockchainNetwork.ETHEREUM });
        Assert.Equal("OK", Block(overview, BlockchainNetwork.TRON).BalancesStatus);
    }

    [Fact]
    public async Task AServiceThatDoesNotAnswer_IsUnavailableForItsNetworkOnly()
    {
        var ethereum = EthereumSidecar();
        ethereum.Balances = BlockchainSidecarBalancesResult.Unavailable;
        var registry = new FakeBlockchainServiceRegistry()
            .With(BlockchainNetwork.TRON, sidecar: TronSidecar())
            .With(BlockchainNetwork.ETHEREUM, sidecar: ethereum);

        await using var ctx = CreateContext();
        var overview = await Service(ctx, registry).GetAsync(CancellationToken.None);

        var e = Block(overview, BlockchainNetwork.ETHEREUM);
        Assert.Equal("UNAVAILABLE", e.BalancesStatus);
        Assert.All(e.Wallets, w => Assert.Null(w.Balances));
        Assert.Equal("OK", Block(overview, BlockchainNetwork.TRON).BalancesStatus);
    }

    [Fact]
    public async Task WhenTheEthereumServiceCannotNameItsFuelWallet_TheWalletIsStillListed()
    {
        var ethereum = EthereumSidecar();
        ethereum.Platform = BlockchainSidecarPlatformWalletsResult.Unavailable;
        var registry = new FakeBlockchainServiceRegistry()
            .With(BlockchainNetwork.ETHEREUM, sidecar: ethereum);

        await using var ctx = CreateContext();
        var e = Block(await Service(ctx, registry).GetAsync(CancellationToken.None), BlockchainNetwork.ETHEREUM);

        var fuel = e.Wallets.Single(w => w.Role == "FUEL");
        Assert.Null(fuel.Address); // unknown — never a guess
        Assert.Null(fuel.Balances);
        Assert.Equal("0.06", fuel.NativeFloor);
    }

    [Fact]
    public async Task AHotAddressThatDiffersFromTheOneTheServiceSignsWith_IsFlagged()
    {
        var ethereum = EthereumSidecar(hot: "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed");
        var registry = new FakeBlockchainServiceRegistry()
            .With(BlockchainNetwork.ETHEREUM, sidecar: ethereum);

        await using var ctx = CreateContext();
        var e = Block(await Service(ctx, registry).GetAsync(CancellationToken.None), BlockchainNetwork.ETHEREUM);

        Assert.True(e.Wallets.Single(w => w.Role == "HOT").AddressMismatch);
    }

    [Fact]
    public async Task OutboundQueueAndTransfers_AreCountedPerNetwork()
    {
        var tx = NewTransaction(_seller.Id, network: BlockchainNetwork.ETHEREUM);
        var tronTx = NewTransaction(_seller.Id, network: BlockchainNetwork.TRON);
        await using (var seed = CreateContext())
        {
            seed.Set<Transaction>().AddRange(tx, tronTx);
            await seed.SaveChangesAsync();
            seed.Set<BlockchainTransaction>().AddRange(
                Outbound(tx.Id, BlockchainNetwork.ETHEREUM, BlockchainTransactionStatus.PENDING, DateTime.UtcNow.AddHours(-3)),
                Outbound(tx.Id, BlockchainNetwork.ETHEREUM, BlockchainTransactionStatus.PENDING, DateTime.UtcNow.AddHours(-1),
                    type: BlockchainTransactionType.BUYER_REFUND),
                Outbound(tx.Id, BlockchainNetwork.ETHEREUM, BlockchainTransactionStatus.DETECTED, DateTime.UtcNow.AddHours(-2),
                    "0x" + new string('1', 64), BlockchainTransactionType.EXCESS_REFUND),
                Outbound(tronTx.Id, BlockchainNetwork.TRON, BlockchainTransactionStatus.PENDING, DateTime.UtcNow.AddHours(-5)));
            seed.Set<ColdWalletTransfer>().AddRange(
                Cold(BlockchainNetwork.ETHEREUM, 100m, "0x" + new string('2', 64)),
                Cold(BlockchainNetwork.TRON, 5m, "tron-cold-tx"));
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var overview = await Service(ctx, new FakeBlockchainServiceRegistry()).GetAsync(CancellationToken.None);

        var e = Block(overview, BlockchainNetwork.ETHEREUM);
        Assert.Equal(2, e.Outbound.PendingCount);
        Assert.Equal(1, e.Outbound.InFlightCount);
        Assert.NotNull(e.Outbound.OldestPendingAt);
        var cold = Assert.Single(e.RecentColdTransfers);
        Assert.Equal(100m, cold.Amount);
        Assert.Equal("T157 Wallet Admin", cold.InitiatedBy);
        Assert.Equal(3, e.RecentOutbound.Count);

        var t = Block(overview, BlockchainNetwork.TRON);
        Assert.Equal(1, t.Outbound.PendingCount);
        Assert.Equal(0, t.Outbound.InFlightCount);
        Assert.Equal(5m, Assert.Single(t.RecentColdTransfers).Amount);
        Assert.Single(t.RecentOutbound);
    }

    [Fact]
    public async Task T160c_RefundFeeBooks_Show_Withheld_Collected_InTransit_OnDeposits_And_Chain_Cost_Per_Network()
    {
        const string tronDeposit = "TDepositRefundFeeBooks000000000000";
        const string ethDeposit = "0x00000000000000000000000000000000000000b1";
        var tx = NewTransaction(_seller.Id, network: BlockchainNetwork.TRON);
        var ethTx = NewTransaction(_seller.Id, network: BlockchainNetwork.ETHEREUM);
        await using (var seed = CreateContext())
        {
            seed.Set<Transaction>().AddRange(tx, ethTx);
            await seed.SaveChangesAsync();
            var pa = NewPaymentAddress(tx.Id, BlockchainNetwork.TRON, tronDeposit, 9101);
            var ethPa = NewPaymentAddress(ethTx.Id, BlockchainNetwork.ETHEREUM, ethDeposit, 9102);
            seed.Set<PaymentAddress>().AddRange(pa, ethPa);
            await seed.SaveChangesAsync();

            seed.Set<BlockchainTransaction>().AddRange(
                // Withheld on the deposit, carried by the sale's sweep.
                Refund(tx.Id, BlockchainTransactionType.EXCESS_REFUND, tronDeposit, 1.37m, sun: 6_400_000),
                // Sent by the hot wallet: its fee is in the hot wallet already.
                Refund(tx.Id, BlockchainTransactionType.BUYER_REFUND, TronHot, 0.83m, sun: 3_100_000),
                // Withheld on the deposit, collected separately (confirmed).
                Refund(tx.Id, BlockchainTransactionType.LATE_PAYMENT_REFUND, tronDeposit, 2.41m, sun: 13_000_000),
                // Withheld on the deposit, its collection is on its way.
                Refund(tx.Id, BlockchainTransactionType.INCORRECT_AMOUNT_REFUND, tronDeposit, 0.59m, sun: 2_200_000),
                // Withheld on the deposit, not collected yet.
                Refund(tx.Id, BlockchainTransactionType.EXCESS_REFUND, tronDeposit, 0.31m, sun: 1_900_000),
                // Not final: nothing withheld yet.
                Refund(tx.Id, BlockchainTransactionType.EXCESS_REFUND, tronDeposit, 9.99m, sun: null,
                    status: BlockchainTransactionStatus.PENDING),
                Carrier(tx.Id, pa.Id, BlockchainTransactionType.SWEEP, tronDeposit, amount: 52.37m, recovered: 1.37m,
                    BlockchainTransactionStatus.CONFIRMED, sun: 14_000_000),
                Carrier(tx.Id, pa.Id, BlockchainTransactionType.REFUND_FEE_SWEEP, tronDeposit, amount: 2.41m, recovered: 2.41m,
                    BlockchainTransactionStatus.CONFIRMED, sun: 6_270_000),
                Carrier(tx.Id, pa.Id, BlockchainTransactionType.REFUND_FEE_SWEEP, tronDeposit, amount: 0.59m, recovered: 0.59m,
                    BlockchainTransactionStatus.PENDING, sun: null),
                Refund(ethTx.Id, BlockchainTransactionType.BUYER_REFUND, ethDeposit, 0.09m, sun: null,
                    network: BlockchainNetwork.ETHEREUM, wei: 61_000_000_000_000m));
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var overview = await Service(ctx, new FakeBlockchainServiceRegistry()).GetAsync(CancellationToken.None);

        var tron = Block(overview, BlockchainNetwork.TRON).RefundFees;
        Assert.Equal("5.51", tron.Withheld);          // 1.37 + 0.83 + 2.41 + 0.59 + 0.31
        Assert.Equal("4.61", tron.Collected);         // 0.83 (hot wallet) + 1.37 (sweep) + 2.41 (collection)
        Assert.Equal("0.59", tron.InTransit);
        Assert.Equal("0.31", tron.OnDeposits);
        Assert.Equal("26.6", tron.RefundChainCost);   // 6.4 + 3.1 + 13.0 + 2.2 + 1.9 TRX
        Assert.Equal("6.27", tron.CollectionChainCost); // the sale's own sweep is not counted

        var eth = Block(overview, BlockchainNetwork.ETHEREUM).RefundFees;
        Assert.Equal("0.09", eth.Withheld);
        Assert.Equal("0", eth.Collected);
        Assert.Equal("0.09", eth.OnDeposits);
        Assert.Equal("0.000061", eth.RefundChainCost);
        Assert.Equal("0", eth.CollectionChainCost);

        // T160c düzeltme turu — none of these rows has a measured platform
        // spend: the native figures above are the receipt fees as a lower
        // bound, the USDT figures leave them out, and the screen says how many.
        Assert.Equal(6, tron.CostUnmeasured);         // 5 confirmed refunds + 1 confirmed collection
        Assert.Equal("0", tron.RefundChainCostUsdt);
        Assert.Equal("0", tron.CollectionChainCostUsdt);
        Assert.Equal("5.51", tron.NetUsdt);           // an upper bound while CostUnmeasured > 0
        Assert.Equal(1, eth.CostUnmeasured);
    }

    /// <summary>
    /// T160c düzeltme turu (F1, ⚠ PARA) — the expense is what left the
    /// platform's wallets, valued at the price its service reported when the
    /// row was confirmed. Pinned with the T159b rehearsal's measured numbers
    /// (Sepolia, ETH 2,444.861904 USDT; Nile, TRX 0.3313):
    /// <list type="bullet">
    ///   <item>Senaryo 2 — <c>BUYER_REFUND</c>, 0.50 withheld; fuel 168,966,002,534,490
    ///     wei + its 204,600 gas at 1 gwei; refund receipt 152,417 gas. Spend 0.913317
    ///     USDT → a 0.413317 loss; the receipt fee alone (0.372639, 40.8 %) showed a 0.127 profit.</item>
    ///   <item>Senaryo 4c — <c>LATE_PAYMENT_REFUND</c>, 0.92 withheld; fuel ≈ 168,940 × 1.000000015
    ///     gwei + 204,600 gas; receipt 152,393 gas. Spend 0.913254 → about break-even (+0.006746);
    ///     the receipt fee alone showed +0.547.</item>
    ///   <item>TRON senaryo 6 — <c>INCORRECT_AMOUNT_REFUND</c>, 0.37 withheld; the refund itself
    ///     burned 0 (Nile's contract pays the Energy), but the hot wallet activated the deposit
    ///     (1 SUN + 1.1 TRX creation fee) and sent a 1.611499 TRX burn top-up that stayed on the
    ///     deposit: 2,711,500 SUN → 0.898320 USDT, a 0.528320 loss the receipt fee (0) hid.</item>
    /// </list>
    /// The rows go through the production confirmation job (status → spend + price →
    /// <c>PlatformNetworkCostUsdt</c>), then AD38 sums them.
    /// </summary>
    [Fact]
    public async Task T160c_RefundFeeBooks_Expense_Is_What_Left_The_Platform_At_The_Confirmation_Price()
    {
        const string ethDeposit = "0x4d3ba2b63533fdc13e3d85cfd58a941394ddebb2";
        const string ethDeposit4c = "0x4e4741a143d58074d78bac0a7b313b2164ac6369";
        const string tronDeposit = "THmvon1aTzDhURM6LUdQftR8HqkQBkPXoo";
        var eth2 = NewTransaction(_seller.Id, network: BlockchainNetwork.ETHEREUM);
        var eth4c = NewTransaction(_seller.Id, network: BlockchainNetwork.ETHEREUM);
        var tron6 = NewTransaction(_seller.Id, network: BlockchainNetwork.TRON);
        BlockchainTransaction s2, s4c, t6;
        await using (var seed = CreateContext())
        {
            seed.Set<Transaction>().AddRange(eth2, eth4c, tron6);
            await seed.SaveChangesAsync();
            seed.Set<PaymentAddress>().AddRange(
                NewPaymentAddress(eth2.Id, BlockchainNetwork.ETHEREUM, ethDeposit, 9105),
                NewPaymentAddress(eth4c.Id, BlockchainNetwork.ETHEREUM, ethDeposit4c, 9109),
                NewPaymentAddress(tron6.Id, BlockchainNetwork.TRON, tronDeposit, 9106));
            await seed.SaveChangesAsync();

            s2 = Refund(eth2.Id, BlockchainTransactionType.BUYER_REFUND, ethDeposit, 0.50m, sun: null,
                status: BlockchainTransactionStatus.DETECTED, network: BlockchainNetwork.ETHEREUM);
            s4c = Refund(eth4c.Id, BlockchainTransactionType.LATE_PAYMENT_REFUND, ethDeposit4c, 0.92m, sun: null,
                status: BlockchainTransactionStatus.DETECTED, network: BlockchainNetwork.ETHEREUM);
            t6 = Refund(tron6.Id, BlockchainTransactionType.INCORRECT_AMOUNT_REFUND, tronDeposit, 0.37m, sun: null,
                status: BlockchainTransactionStatus.DETECTED);
            // TRON reports the spend with the broadcast (OutgoingTransferDispatchJob).
            t6.PlatformNetworkCost = 2_711_500m;
            seed.Set<BlockchainTransaction>().AddRange(s2, s4c, t6);
            await seed.SaveChangesAsync();
        }

        var ethStatuses = new HashStatusStub(new()
        {
            [s2.TxHash!] = new(TransferStatusOutcome.Confirmed, 11_959_101, 64, "SUCCESS", null,
                RealizedFeeWei: 152_417m * 1_000_000_000m,
                PlatformSpendWei: 168_966_002_534_490m + 204_600m * 1_000_000_000m,
                NativePriceUsdt: 2444.861904m),
            [s4c.TxHash!] = new(TransferStatusOutcome.Confirmed, 11_959_101, 64, "SUCCESS", null,
                RealizedFeeWei: 152_393m * 1_000_000_000m,
                PlatformSpendWei: 168_940_002_534_100m + 204_600m * 1_000_000_000m,
                NativePriceUsdt: 2444.861904m),
        });
        var tronStatuses = new HashStatusStub(new()
        {
            [t6.TxHash!] = new(TransferStatusOutcome.Confirmed, 72_004_108, 20, "SUCCESS", null,
                RealizedFeeSun: 0, NativePriceUsdt: 0.3313m),
        });
        var registry = new FakeBlockchainServiceRegistry()
            .With(BlockchainNetwork.ETHEREUM, transfers: ethStatuses)
            .With(BlockchainNetwork.TRON, transfers: tronStatuses);
        await using (var ctx = CreateContext())
        {
            await new OutgoingTransferConfirmationJob(
                    ctx, registry, AlwaysHealthyBlockchainNetworks.Instance, new OutboxService(ctx),
                    StablecoinContractRegistry.FromOptions(new StablecoinContractOptions()),
                    new StubPlatformWalletAddressProvider(),
                    TimeProvider.System, NullLogger<OutgoingTransferConfirmationJob>.Instance)
                .ExecuteAsync();
        }

        await using (var verify = CreateContext())
        {
            var rows = await verify.Set<BlockchainTransaction>().AsNoTracking()
                .Where(b => b.Id == s2.Id || b.Id == s4c.Id || b.Id == t6.Id)
                .ToDictionaryAsync(b => b.Id);
            Assert.All(rows.Values, r => Assert.Equal(BlockchainTransactionStatus.CONFIRMED, r.Status));
            Assert.Equal(373_566_002_534_490m, rows[s2.Id].PlatformNetworkCost);
            Assert.Equal(0.913317m, rows[s2.Id].PlatformNetworkCostUsdt);
            Assert.Equal(152_417_000_000_000m, rows[s2.Id].RealizedFeeWei);
            Assert.Equal(0.913254m, rows[s4c.Id].PlatformNetworkCostUsdt);
            Assert.Equal(2_711_500m, rows[t6.Id].PlatformNetworkCost);   // kept from the broadcast
            Assert.Equal(0.898320m, rows[t6.Id].PlatformNetworkCostUsdt);
            Assert.Equal(0L, rows[t6.Id].RealizedFeeSun);
        }

        await using var read = CreateContext();
        var overview = await Service(read, new FakeBlockchainServiceRegistry()).GetAsync(CancellationToken.None);

        var eth = Block(overview, BlockchainNetwork.ETHEREUM).RefundFees;
        Assert.Equal("1.42", eth.Withheld);
        Assert.Equal("0.00074710600506859", eth.RefundChainCost);  // not the receipts' 0.00030481
        Assert.Equal("1.826571", eth.RefundChainCostUsdt);
        Assert.Equal("-0.406571", eth.NetUsdt);                    // the receipt fees alone: +0.674782
        Assert.Equal(0, eth.CostUnmeasured);

        var tron = Block(overview, BlockchainNetwork.TRON).RefundFees;
        Assert.Equal("0.37", tron.Withheld);
        Assert.Equal("2.7115", tron.RefundChainCost);              // the receipt: 0
        Assert.Equal("0.89832", tron.RefundChainCostUsdt);
        Assert.Equal("-0.52832", tron.NetUsdt);
        Assert.Equal(0, tron.CostUnmeasured);
    }

    /// <summary>
    /// T160c kapanış (S4-2, ⚠ PARA) — the net is withheld minus BOTH expenses:
    /// a separate collection's measured spend is subtracted too. 1.37 withheld;
    /// the refund cost 0.898320 (Nile senaryo 6); the sale's sweep carried
    /// 0.40 of it at no cost, the confirmed collection the other 0.97 for
    /// 2.000003 TRX = 0.662601 → net −0.190921. Without the collection term it
    /// would read +0.47168 (a profit). The sale's own sweep is never an expense.
    /// </summary>
    [Fact]
    public async Task T160c_RefundFeeBooks_Net_Subtracts_The_Measured_Collection_Spend_Too()
    {
        const string tronDeposit = "TDepositNetCollection0000000000000";
        var tx = NewTransaction(_seller.Id, network: BlockchainNetwork.TRON);
        await using (var seed = CreateContext())
        {
            seed.Set<Transaction>().Add(tx);
            await seed.SaveChangesAsync();
            var pa = NewPaymentAddress(tx.Id, BlockchainNetwork.TRON, tronDeposit, 9111);
            seed.Set<PaymentAddress>().Add(pa);
            await seed.SaveChangesAsync();

            var refund = Refund(tx.Id, BlockchainTransactionType.INCORRECT_AMOUNT_REFUND, tronDeposit, 1.37m, sun: 0);
            refund.PlatformNetworkCost = 2_711_500m;
            refund.PlatformNetworkCostUsdt = 0.898320m;
            var collection = Carrier(tx.Id, pa.Id, BlockchainTransactionType.REFUND_FEE_SWEEP, tronDeposit,
                amount: 0.97m, recovered: 0.97m, BlockchainTransactionStatus.CONFIRMED, sun: 0);
            collection.PlatformNetworkCost = 2_000_003m;
            collection.PlatformNetworkCostUsdt = 0.662601m;
            // A sale's sweep with a measured spend: not an expense of the refund fees.
            var sweep = Carrier(tx.Id, pa.Id, BlockchainTransactionType.SWEEP, tronDeposit,
                amount: 50.40m, recovered: 0.40m, BlockchainTransactionStatus.CONFIRMED, sun: 0);
            sweep.PlatformNetworkCost = 8_438_350m;
            sweep.PlatformNetworkCostUsdt = 2.795626m;
            seed.Set<BlockchainTransaction>().AddRange(refund, collection, sweep);
            await seed.SaveChangesAsync();
        }

        await using var ctx = CreateContext();
        var overview = await Service(ctx, new FakeBlockchainServiceRegistry()).GetAsync(CancellationToken.None);

        var tron = Block(overview, BlockchainNetwork.TRON).RefundFees;
        Assert.Equal("1.37", tron.Withheld);
        Assert.Equal("1.37", tron.Collected);
        Assert.Equal("2.7115", tron.RefundChainCost);
        Assert.Equal("0.89832", tron.RefundChainCostUsdt);
        Assert.Equal("2.000003", tron.CollectionChainCost);
        Assert.Equal("0.662601", tron.CollectionChainCostUsdt);
        Assert.Equal("-0.190921", tron.NetUsdt);
        Assert.Equal(0, tron.CostUnmeasured);
    }

    /// <summary>Answers each hash with its own status (T160c düzeltme turu).</summary>
    private sealed class HashStatusStub(Dictionary<string, TransferStatusResult> statuses) : IBlockchainTransferClient
    {
        public Task<TransferBroadcastResult> BroadcastAsync(TransferBroadcastRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TransferStatusResult> GetStatusAsync(string txHash, CancellationToken cancellationToken) =>
            Task.FromResult(statuses[txHash]);
    }

    private static BlockchainTransaction Refund(
        Guid transactionId, BlockchainTransactionType type, string from, decimal fee, long? sun,
        BlockchainTransactionStatus status = BlockchainTransactionStatus.CONFIRMED,
        BlockchainNetwork network = BlockchainNetwork.TRON, decimal? wei = null) => new()
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            Network = network,
            Type = type,
            TxHash = status == BlockchainTransactionStatus.PENDING ? null : "refund-" + Guid.NewGuid().ToString("N"),
            FromAddress = from,
            ToAddress = network == BlockchainNetwork.TRON ? SellerWallet : EthColdLower,
            Amount = 10m,
            GasFee = fee,
            Token = StablecoinType.USDT,
            Status = status,
            ConfirmationCount = status == BlockchainTransactionStatus.CONFIRMED ? 20 : 0,
            ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED ? DateTime.UtcNow : null,
            RealizedFeeSun = sun,
            RealizedFeeWei = wei,
            CreatedAt = DateTime.UtcNow,
        };

    private static BlockchainTransaction Carrier(
        Guid transactionId, Guid paymentAddressId, BlockchainTransactionType type, string from,
        decimal amount, decimal recovered, BlockchainTransactionStatus status, long? sun) => new()
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            PaymentAddressId = paymentAddressId,
            Network = BlockchainNetwork.TRON,
            Type = type,
            TxHash = status == BlockchainTransactionStatus.PENDING ? null : "carry-" + Guid.NewGuid().ToString("N"),
            FromAddress = from,
            ToAddress = TronHot,
            Amount = amount,
            RefundFeeRecovered = recovered,
            Token = StablecoinType.USDT,
            Status = status,
            ConfirmationCount = status == BlockchainTransactionStatus.CONFIRMED ? 20 : 0,
            ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED ? DateTime.UtcNow : null,
            RealizedFeeSun = sun,
            CreatedAt = DateTime.UtcNow,
        };

    private BlockchainTransaction Outbound(
        Guid transactionId, BlockchainNetwork network, BlockchainTransactionStatus status,
        DateTime createdAt, string? hash = null,
        BlockchainTransactionType type = BlockchainTransactionType.SELLER_PAYOUT) => new()
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            Network = network,
            Type = type,
            TxHash = hash,
            FromAddress = network == BlockchainNetwork.TRON ? TronHot : EthHotLower,
            ToAddress = network == BlockchainNetwork.TRON ? SellerWallet : EthColdLower,
            Amount = 50m,
            Token = StablecoinType.USDT,
            Status = status,
            CreatedAt = createdAt,
        };

    private ColdWalletTransfer Cold(BlockchainNetwork network, decimal amount, string hash) => new()
    {
        Network = network,
        Amount = amount,
        Token = StablecoinType.USDT,
        FromAddress = network == BlockchainNetwork.TRON ? TronHot : EthHotLower,
        ToAddress = network == BlockchainNetwork.TRON ? TronCold : EthColdLower,
        TxHash = hash,
        InitiatedByAdminId = _admin.Id,
        CreatedAt = DateTime.UtcNow,
    };

    private sealed class StubWalletProvider : IPlatformWalletAddressProvider
    {
        public string? HotWalletAddress { get; set; }
        public string? ColdWalletAddress { get; set; }
        public Dictionary<BlockchainNetwork, PlatformWalletAddresses> Pairs { get; } = new();

        public PlatformWalletAddresses For(BlockchainNetwork network) =>
            Pairs.TryGetValue(network, out var pair)
                ? pair
                : network == BlockchainNetwork.TRON
                    ? new PlatformWalletAddresses(network, HotWalletAddress, ColdWalletAddress)
                    : new PlatformWalletAddresses(network, null, null);
    }

    private sealed class OverviewSidecar : IBlockchainSidecarClient
    {
        public BlockchainSidecarBalancesResult Balances { get; set; } = BlockchainSidecarBalancesResult.Unavailable;
        public BlockchainSidecarPlatformWalletsResult Platform { get; set; } = BlockchainSidecarPlatformWalletsResult.NotAsked;
        public List<string> QueriedAddresses { get; } = [];

        public Task<BlockchainSidecarBalancesResult> GetWalletBalancesAsync(
            IReadOnlyList<string> addresses, CancellationToken cancellationToken)
        {
            QueriedAddresses.AddRange(addresses);
            return Task.FromResult(Balances);
        }

        public Task<BlockchainSidecarPlatformWalletsResult> GetPlatformWalletsAsync(
            CancellationToken cancellationToken) => Task.FromResult(Platform);

        public Task<BlockchainSidecarDeriveResult> DeriveAddressAsync(int index, Guid transactionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<BlockchainSidecarStatus> StartMonitoringAsync(PaymentMonitorStartRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<BlockchainSidecarStatus> StopMonitoringAsync(string address, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<BlockchainSidecarStatus> StartPostCancelMonitoringAsync(PostCancelMonitorStartRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<BlockchainSidecarStatus> StopPostCancelMonitoringAsync(string address, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<BlockchainSidecarTransferResult> SendHotToColdTransferAsync(HotToColdTransferRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
