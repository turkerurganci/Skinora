using Microsoft.Extensions.Logging.Abstractions;
using Skinora.API.Services.AdminWallets;
using Skinora.API.Tests.Common;
using Skinora.Payments.Domain.Entities;
using Skinora.Platform.Application.Wallets;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
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
            new(TronHot, new Dictionary<string, string> { ["TRX"] = "250000000", ["USDT"] = "1500250000", ["USDC"] = "0" }),
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
        Assert.Equal("250", Balance(tHot, "TRX"));
        Assert.Equal("1500.25", Balance(tHot, "USDT"));
        Assert.Equal("100", tHot.NativeFloor); // 100 TRX seeded floor
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
