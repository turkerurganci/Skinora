using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Platform.Application.Wallets;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Transactions.Tests.Helpers;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Transfers;

/// <summary>
/// T160c — <see cref="RefundFeeSweepQueueJob"/>: the refund network fees
/// withheld from buyers that the sale's own sweep could not carry are moved
/// deposit → hot wallet, two networks, USDT and USDC, only when collecting
/// costs less than it brings, never twice, never while anything else leaves
/// the deposit.
/// </summary>
/// <remarks>
/// Fixture fees (3.11, 1.37, 0.83, 0.29 …) and estimates (1.20, 1.60 …) differ
/// from every fallback / default (TRON refund fallback 2.0, Ethereum 0.05,
/// the 2.0 refund-threshold ratio), so a constant leaking in instead of the
/// ledger would show.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class RefundFeeSweepQueueJobTests : IDisposable
{
    static RefundFeeSweepQueueJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string TronHot = "THotWalletFeeSweepFixture0000000000";
    private const string EthHot = "0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed";
    private const string TronUsdc = "TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8";
    private const string EthUsdc = "0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48";

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly FakeTimeProvider _clock = new();
    private readonly StubWalletAddresses _wallets = new() { Tron = TronHot, Ethereum = EthHot };
    private readonly StubEstimator _tronEstimator = new() { Fee = 1.20m };
    private readonly StubEstimator _ethEstimator = new() { Fee = 0.04m };
    private readonly StubSettings _settings = new();
    private readonly FakeBlockchainServiceRegistry _registry;
    private readonly RefundFeeSweepQueueJob _sut;
    private int _seq;

    public RefundFeeSweepQueueJobTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _clock.SetUtcNow(new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));
        _registry = new FakeBlockchainServiceRegistry()
            .With(BlockchainNetwork.TRON, gasFee: _tronEstimator)
            .With(BlockchainNetwork.ETHEREUM, gasFee: _ethEstimator);
        _sut = new RefundFeeSweepQueueJob(
            _db, _wallets, _registry, _settings, new StubContracts(), _clock,
            NullLogger<RefundFeeSweepQueueJob>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    // ---------------------------------------------------------------- collects

    [Theory]
    [InlineData(TransactionStatus.CANCELLED_TIMEOUT, BlockchainTransactionType.BUYER_REFUND)]
    [InlineData(TransactionStatus.CANCELLED_ADMIN, BlockchainTransactionType.BUYER_REFUND)]
    [InlineData(TransactionStatus.REFUNDED, BlockchainTransactionType.BUYER_REFUND)]
    [InlineData(TransactionStatus.CANCELLED_TIMEOUT, BlockchainTransactionType.INCORRECT_AMOUNT_REFUND)]
    [InlineData(TransactionStatus.CANCELLED_SELLER, BlockchainTransactionType.LATE_PAYMENT_REFUND)]
    public async Task A_Deposit_That_Never_Reaches_A_Sweep_Has_Its_Withheld_Fee_Collected(
        TransactionStatus status, BlockchainTransactionType refundType)
    {
        var (tx, deposit) = await SeedAsync(status);
        AddRefund(tx, deposit, refundType, net: 18.89m, fee: 3.11m);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var row = await SingleCollectionAsync(tx);
        Assert.Equal(BlockchainTransactionStatus.PENDING, row.Status);
        Assert.Equal(3.11m, row.Amount);
        Assert.Equal(3.11m, row.RefundFeeRecovered);
        Assert.Equal(StablecoinType.USDT, row.Token);
        Assert.Equal(BlockchainNetwork.TRON, row.Network);
        Assert.Equal(deposit.Id, row.PaymentAddressId);
        Assert.Equal(deposit.Address, row.FromAddress);
        Assert.Equal(TronHot, row.ToAddress);
        Assert.Null(row.ActualTokenAddress);
        Assert.Null(row.GasFee);
        // The collection was priced as exactly this transfer: deposit → hot wallet.
        var asked = Assert.Single(_tronEstimator.Requests);
        Assert.Equal(new GasFeeEstimateRequest(deposit.Address, TronHot, 3.11m, StablecoinType.USDT), asked);
    }

    [Fact]
    public async Task Several_Refunds_On_One_Deposit_Are_Collected_In_One_Transfer()
    {
        var (tx, deposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT);
        AddRefund(tx, deposit, BlockchainTransactionType.INCORRECT_AMOUNT_REFUND, net: 6.63m, fee: 1.37m);
        AddRefund(tx, deposit, BlockchainTransactionType.LATE_PAYMENT_REFUND, net: 9.17m, fee: 0.83m);
        AddRefund(tx, deposit, BlockchainTransactionType.LATE_PAYMENT_REFUND, net: 4.71m, fee: 1.29m);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.Equal(3.49m, (await SingleCollectionAsync(tx)).Amount);
    }

    [Fact]
    public async Task Ethereum_Collects_On_Ethereum_With_Its_Own_Estimator_And_Hot_Wallet()
    {
        var (tx, deposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT, BlockchainNetwork.ETHEREUM);
        AddRefund(tx, deposit, BlockchainTransactionType.BUYER_REFUND, net: 20.31m, fee: 0.09m);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var row = await SingleCollectionAsync(tx);
        Assert.Equal(BlockchainNetwork.ETHEREUM, row.Network);
        Assert.Equal(EthHot, row.ToAddress);
        Assert.Equal(0.09m, row.Amount);
        Assert.Single(_ethEstimator.Requests);
        Assert.Empty(_tronEstimator.Requests);
    }

    [Theory]
    [InlineData(BlockchainNetwork.TRON, TronUsdc)]
    [InlineData(BlockchainNetwork.ETHEREUM, EthUsdc)]
    public async Task A_Wrong_Token_Refunds_Fee_Is_Collected_In_The_Token_It_Was_Withheld_In_After_The_Sweep(
        BlockchainNetwork network, string usdcContract)
    {
        _tronEstimator.Fee = 0.15m; // < 0.37
        var (tx, deposit) = await SeedAsync(TransactionStatus.COMPLETED, network);
        AddSweep(tx, deposit, BlockchainTransactionStatus.CONFIRMED, recovered: null);
        var wrong = AddRefund(tx, deposit, BlockchainTransactionType.WRONG_TOKEN_REFUND, net: 9.63m, fee: 0.37m);
        wrong.ActualTokenAddress = usdcContract;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var row = await SingleCollectionAsync(tx);
        Assert.Equal(StablecoinType.USDC, row.Token);
        Assert.Equal(0.37m, row.Amount);
        Assert.Null(row.ActualTokenAddress);
    }

    [Fact]
    public async Task A_Refund_Final_Only_After_The_Sweep_Was_Queued_Is_Collected_Once_The_Sweep_Is_Final()
    {
        var (tx, deposit) = await SeedAsync(TransactionStatus.COMPLETED);
        // The sweep carried the first fee (1.37); the second refund confirmed later.
        AddSweep(tx, deposit, BlockchainTransactionStatus.CONFIRMED, recovered: 1.37m);
        AddRefund(tx, deposit, BlockchainTransactionType.EXCESS_REFUND, net: 5.63m, fee: 1.37m);
        AddRefund(tx, deposit, BlockchainTransactionType.EXCESS_REFUND, net: 2.17m, fee: 2.83m);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.Equal(2.83m, (await SingleCollectionAsync(tx)).Amount);
    }

    // ------------------------------------------------------------ economics

    [Fact]
    public async Task A_Cent_Above_The_Cost_It_Goes_At_The_Cost_It_Waits()
    {
        // The refund-threshold ratio (2.5 here) is NOT the rule: collecting
        // pays as soon as it brings more than it costs.
        _settings.TronRatio = 2.5m;
        _tronEstimator.Fee = 3.09m;
        var (aboveTx, aboveDeposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT);
        AddRefund(aboveTx, aboveDeposit, BlockchainTransactionType.BUYER_REFUND, net: 18.9m, fee: 3.10m);
        var (atTx, atDeposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT);
        AddRefund(atTx, atDeposit, BlockchainTransactionType.BUYER_REFUND, net: 18.91m, fee: 3.09m);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.Equal(3.10m, (await SingleCollectionAsync(aboveTx)).Amount);
        Assert.False(await AnyCollectionAsync(atTx));
        Assert.Equal(Now + RefundFeeSweepQueueJob.UneconomicRecheck, await NextCheckAsync(atDeposit));
        Assert.Null(await NextCheckAsync(aboveDeposit));
    }

    [Fact]
    public async Task A_Free_Collection_Delegated_Energy_Goes()
    {
        _tronEstimator.Fee = 0m;
        var (tx, deposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT);
        AddRefund(tx, deposit, BlockchainTransactionType.BUYER_REFUND, net: 18.89m, fee: 0.07m);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.Equal(0.07m, (await SingleCollectionAsync(tx)).Amount);
    }

    [Fact]
    public async Task Not_Worth_It_Now_Stays_On_The_Deposit_Is_Not_Priced_Again_Until_Due_And_Goes_When_Cheaper()
    {
        _tronEstimator.Fee = 3.47m; // costs more than the 3.11 it would bring
        var (tx, deposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT);
        AddRefund(tx, deposit, BlockchainTransactionType.BUYER_REFUND, net: 18.89m, fee: 3.11m);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();
        Assert.False(await AnyCollectionAsync(tx));
        Assert.Single(_tronEstimator.Requests);

        // Before it is due: not even priced (TronGrid quota).
        _clock.Advance(RefundFeeSweepQueueJob.UneconomicRecheck - TimeSpan.FromMinutes(1));
        await _sut.ExecuteAsync();
        Assert.Single(_tronEstimator.Requests);

        // Due, and the chain got cheaper: collected.
        _clock.Advance(TimeSpan.FromMinutes(1));
        _tronEstimator.Fee = 1.10m;
        await _sut.ExecuteAsync();
        Assert.Equal(2, _tronEstimator.Requests.Count);
        Assert.Equal(3.11m, (await SingleCollectionAsync(tx)).Amount);
    }

    [Theory]
    [InlineData(null)]       // the estimate is unavailable
    [InlineData(10.01)]      // above the TRON sanity ceiling (10.0) — not believed
    [InlineData(-0.01)]      // nonsense
    public async Task An_Untrusted_Or_Missing_Price_Queues_Nothing_And_Asks_Again_Soon(double? fee)
    {
        _tronEstimator.Fee = fee is null ? null : (decimal)fee;
        var (tx, deposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT);
        AddRefund(tx, deposit, BlockchainTransactionType.BUYER_REFUND, net: 18.89m, fee: 3.11m);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.False(await AnyCollectionAsync(tx));
        Assert.Equal(Now + RefundFeeSweepQueueJob.UnpricedRecheck, await NextCheckAsync(deposit));
    }

    [Fact]
    public async Task An_Estimator_That_Throws_Is_Treated_As_No_Price()
    {
        _tronEstimator.Throw = true;
        var (tx, deposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT);
        AddRefund(tx, deposit, BlockchainTransactionType.BUYER_REFUND, net: 18.89m, fee: 3.11m);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.False(await AnyCollectionAsync(tx));
        Assert.Equal(Now + RefundFeeSweepQueueJob.UnpricedRecheck, await NextCheckAsync(deposit));
    }

    // ------------------------------------------------------------- not now

    [Theory]
    [InlineData(BlockchainTransactionType.LATE_PAYMENT_REFUND, BlockchainTransactionStatus.PENDING)]
    [InlineData(BlockchainTransactionType.LATE_PAYMENT_REFUND, BlockchainTransactionStatus.DETECTED)]
    [InlineData(BlockchainTransactionType.BUYER_REFUND, BlockchainTransactionStatus.PENDING)]
    [InlineData(BlockchainTransactionType.REFUND_FEE_SWEEP, BlockchainTransactionStatus.DETECTED)]
    public async Task Nothing_Is_Queued_While_Another_Transfer_Leaves_The_Deposit(
        BlockchainTransactionType inFlightType, BlockchainTransactionStatus inFlightStatus)
    {
        var (tx, deposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT);
        AddRefund(tx, deposit, BlockchainTransactionType.INCORRECT_AMOUNT_REFUND, net: 6.63m, fee: 1.37m);
        if (inFlightType == BlockchainTransactionType.REFUND_FEE_SWEEP)
        {
            AddCollection(tx, deposit, 0.29m, inFlightStatus);
            AddRefund(tx, deposit, BlockchainTransactionType.LATE_PAYMENT_REFUND, net: 9.71m, fee: 0.29m);
        }
        else
        {
            AddRefund(tx, deposit, inFlightType, net: 9.17m, fee: 0.83m, status: inFlightStatus);
        }
        await _db.SaveChangesAsync();
        var before = await _db.Set<BlockchainTransaction>().CountAsync(b => b.Type == BlockchainTransactionType.REFUND_FEE_SWEEP);

        await _sut.ExecuteAsync();

        Assert.Equal(before, await _db.Set<BlockchainTransaction>().CountAsync(b => b.Type == BlockchainTransactionType.REFUND_FEE_SWEEP));
        Assert.Empty(_tronEstimator.Requests);
    }

    [Theory]
    [InlineData(TransactionStatus.SELLER_CONFIRMED)]
    [InlineData(TransactionStatus.PAYMENT_RECEIVED)]
    [InlineData(TransactionStatus.ITEM_DELIVERED)]
    public async Task A_Live_Sale_Waits_For_Its_Own_Sweep_Which_Carries_The_Fee_For_Free(TransactionStatus status)
    {
        var (tx, deposit) = await SeedAsync(status);
        AddRefund(tx, deposit, BlockchainTransactionType.EXCESS_REFUND, net: 5.63m, fee: 1.37m);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.False(await AnyCollectionAsync(tx));
        Assert.Empty(_tronEstimator.Requests);
    }

    [Theory]
    [InlineData(BlockchainTransactionStatus.PENDING)]
    [InlineData(BlockchainTransactionStatus.DETECTED)]
    [InlineData(BlockchainTransactionStatus.FAILED)]
    public async Task A_Sweep_That_Is_Not_Confirmed_Leaves_The_Deposit_Alone(BlockchainTransactionStatus sweepStatus)
    {
        var (tx, deposit) = await SeedAsync(TransactionStatus.ITEM_DELIVERED);
        AddSweep(tx, deposit, sweepStatus, recovered: null);
        var wrong = AddRefund(tx, deposit, BlockchainTransactionType.WRONG_TOKEN_REFUND, net: 9.63m, fee: 0.37m);
        wrong.ActualTokenAddress = TronUsdc;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.False(await AnyCollectionAsync(tx));
    }

    /// <summary>
    /// T160c düzeltme turu (F3) — the sweep must be CONFIRMED. A FAILED sweep
    /// that had folded the fee in (the admin re-drives it: its Amount still
    /// counts the fee) must not be "collected" from under it, and here the
    /// collection WOULD pay (1.37 withheld &gt; 1.20 estimated) — so only the
    /// CONFIRMED rule stops it. The control below is the same deposit with a
    /// confirmed sweep that carried nothing: the same fee is collected.
    /// </summary>
    [Theory]
    [InlineData(TransactionStatus.ITEM_DELIVERED)]
    [InlineData(TransactionStatus.COMPLETED)]
    public async Task A_Failed_Sweep_Keeps_Its_Folded_Fee_Even_When_Collecting_Would_Pay(TransactionStatus status)
    {
        var (tx, deposit) = await SeedAsync(status);
        AddSweep(tx, deposit, BlockchainTransactionStatus.FAILED, recovered: 1.37m);
        AddRefund(tx, deposit, BlockchainTransactionType.EXCESS_REFUND, net: 5.63m, fee: 1.37m);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.False(await AnyCollectionAsync(tx));
        Assert.Empty(_tronEstimator.Requests); // not even priced
    }

    [Fact]
    public async Task Control_The_Same_Fee_Behind_A_Confirmed_Sweep_That_Carried_Nothing_Is_Collected()
    {
        var (tx, deposit) = await SeedAsync(TransactionStatus.COMPLETED);
        AddSweep(tx, deposit, BlockchainTransactionStatus.CONFIRMED, recovered: null);
        AddRefund(tx, deposit, BlockchainTransactionType.EXCESS_REFUND, net: 5.63m, fee: 1.37m);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.Equal(1.37m, (await SingleCollectionAsync(tx)).Amount);
    }

    [Fact]
    public async Task A_Failed_Collection_Is_Not_Retried_Under_A_New_Id()
    {
        var (tx, deposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT);
        AddRefund(tx, deposit, BlockchainTransactionType.BUYER_REFUND, net: 18.89m, fee: 3.11m);
        AddCollection(tx, deposit, 3.11m, BlockchainTransactionStatus.FAILED);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.Equal(1, await _db.Set<BlockchainTransaction>().CountAsync(b => b.Type == BlockchainTransactionType.REFUND_FEE_SWEEP));
    }

    [Fact]
    public async Task Held_Or_Disputed_Transactions_And_Networks_Without_A_Hot_Wallet_Are_Skipped()
    {
        var (held, heldDeposit) = await SeedAsync(TransactionStatus.CANCELLED_ADMIN, configure: t =>
        {
            t.IsOnHold = true;
            t.EmergencyHoldAt = Now;
            t.EmergencyHoldReason = "fixture";
            t.EmergencyHoldByAdminId = t.SellerId;
            t.TimeoutFrozenAt = Now;
            t.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
            t.TimeoutRemainingSeconds = 60;
        });
        AddRefund(held, heldDeposit, BlockchainTransactionType.BUYER_REFUND, net: 18.89m, fee: 3.11m);
        var (disputed, disputedDeposit) = await SeedAsync(TransactionStatus.REFUNDED, configure: t => t.HasActiveDispute = true);
        AddRefund(disputed, disputedDeposit, BlockchainTransactionType.BUYER_REFUND, net: 18.89m, fee: 3.11m);
        var (eth, ethDeposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT, BlockchainNetwork.ETHEREUM);
        AddRefund(eth, ethDeposit, BlockchainTransactionType.BUYER_REFUND, net: 20.31m, fee: 0.09m);
        await _db.SaveChangesAsync();
        _wallets.Ethereum = null;

        await _sut.ExecuteAsync();

        Assert.False(await _db.Set<BlockchainTransaction>().AnyAsync(b => b.Type == BlockchainTransactionType.REFUND_FEE_SWEEP));
    }

    [Fact]
    public async Task A_Fee_The_Hot_Wallet_Kept_Or_A_Refund_That_Moved_Nothing_Is_Not_Owed()
    {
        var (tx, deposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT, BlockchainNetwork.ETHEREUM);
        // Ethereum BUYER_REFUND after the sweep leaves the hot wallet (T158 (f)).
        AddRefund(tx, deposit, BlockchainTransactionType.BUYER_REFUND, net: 20.31m, fee: 0.09m, from: EthHot);
        AddRefund(tx, deposit, BlockchainTransactionType.LATE_PAYMENT_REFUND, net: 9.17m, fee: 0.83m,
            status: BlockchainTransactionStatus.FAILED);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.False(await AnyCollectionAsync(tx));
        Assert.Empty(_ethEstimator.Requests);
    }

    // ------------------------------------------------- never twice / per token

    [Fact]
    public async Task Running_Again_Never_Queues_A_Second_Collection_And_A_Later_Refund_Adds_Only_Its_Own_Fee()
    {
        var (tx, deposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT);
        AddRefund(tx, deposit, BlockchainTransactionType.BUYER_REFUND, net: 18.89m, fee: 3.11m);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();
        await _sut.ExecuteAsync(); // in flight: nothing new
        var first = await SingleCollectionAsync(tx);

        await ConfirmAsync(first.Id);
        await _sut.ExecuteAsync(); // fully collected: nothing new
        Assert.Equal(1, await CollectionCountAsync(tx));

        // A late payment arrives after the collection and is refunded minus its fee.
        AddRefund(tx, deposit, BlockchainTransactionType.LATE_PAYMENT_REFUND, net: 11.03m, fee: 2.97m);
        await _db.SaveChangesAsync();
        await _sut.ExecuteAsync();

        var rows = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .Where(b => b.TransactionId == tx.Id && b.Type == BlockchainTransactionType.REFUND_FEE_SWEEP)
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Amount == 2.97m && r.Status == BlockchainTransactionStatus.PENDING);
    }

    [Fact]
    public async Task Usdt_And_Usdc_Fees_Go_One_Token_At_A_Time_The_Larger_First()
    {
        var (tx, deposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT);
        AddRefund(tx, deposit, BlockchainTransactionType.BUYER_REFUND, net: 18.89m, fee: 3.11m);
        var wrong = AddRefund(tx, deposit, BlockchainTransactionType.WRONG_TOKEN_REFUND, net: 6.39m, fee: 2.61m);
        wrong.ActualTokenAddress = TronUsdc;
        _tronEstimator.Fee = 1.05m;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();
        var usdt = await SingleCollectionAsync(tx);
        Assert.Equal(StablecoinType.USDT, usdt.Token);
        Assert.Equal(3.11m, usdt.Amount);

        await ConfirmAsync(usdt.Id);
        await _sut.ExecuteAsync();

        var usdc = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == tx.Id && b.Type == BlockchainTransactionType.REFUND_FEE_SWEEP
                && b.Status == BlockchainTransactionStatus.PENDING);
        Assert.Equal(StablecoinType.USDC, usdc.Token);
        Assert.Equal(2.61m, usdc.Amount);
    }

    [Fact]
    public async Task The_Database_Refuses_A_Second_In_Flight_Collection_For_A_Transaction()
    {
        var (tx, deposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT);
        AddCollection(tx, deposit, 1.37m, BlockchainTransactionStatus.PENDING);
        await _db.SaveChangesAsync();
        AddCollection(tx, deposit, 0.83m, BlockchainTransactionStatus.PENDING);

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_Database_Refuses_A_Collection_Whose_Recovered_Part_Is_Not_Its_Amount()
    {
        var (tx, deposit) = await SeedAsync(TransactionStatus.CANCELLED_TIMEOUT);
        var row = AddCollection(tx, deposit, 1.37m, BlockchainTransactionStatus.PENDING);
        row.RefundFeeRecovered = 1.36m;

        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    // ------------------------------------------------------------- helpers

    private async Task<(Transaction Tx, PaymentAddress Deposit)> SeedAsync(
        TransactionStatus status,
        BlockchainNetwork network = BlockchainNetwork.TRON,
        Action<Transaction>? configure = null)
    {
        var seq = ++_seq;
        var seller = new User { Id = Guid.NewGuid(), SteamId = $"7656119801{seq * 2:D7}", SteamDisplayName = "Seller", CreatedAt = Now };
        var buyer = new User { Id = Guid.NewGuid(), SteamId = $"7656119801{(seq * 2) + 1:D7}", SteamDisplayName = "Buyer", CreatedAt = Now };
        _db.Set<User>().AddRange(seller, buyer);
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = seller.Id,
            BuyerId = buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000923",
            BuyerRefundAddress = network == BlockchainNetwork.TRON
                ? "TBuyerRefund000000000000000000000000"
                : "0x2222222222222222222222222222222222222222",
            ItemAssetId = "asset-1",
            ItemClassId = "cls",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 21m,
            CommissionRate = 0.02m,
            CommissionAmount = 0.42m,
            TotalAmount = 21.42m,
            SellerPayoutAddress = "TSellerPayout00000000000000000000000",
            CreatedAt = Now.AddMinutes(-seq),
        };
        if (RefundFeeSweepQueueJob.TerminalWithoutSweep.Contains(status))
        {
            tx.CancelledBy = CancelledByType.TIMEOUT;
            tx.CancelReason = "fixture";
            tx.CancelledAt = Now.AddHours(-1);
        }
        configure?.Invoke(tx);
        _db.Set<Transaction>().Add(tx);
        var deposit = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Network = network,
            Address = network == BlockchainNetwork.TRON
                ? $"TDepositFeeSweepFixture{seq:D12}"
                : $"0x{seq:D40}",
            HdWalletIndex = 40 + seq,
            ExpectedAmount = 21.42m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.STOPPED,
            CreatedAt = Now,
            UpdatedAt = Now,
            RowVersion = new byte[8],
        };
        _db.Set<PaymentAddress>().Add(deposit);
        await _db.SaveChangesAsync();
        return (tx, deposit);
    }

    private BlockchainTransaction AddRefund(
        Transaction tx,
        PaymentAddress deposit,
        BlockchainTransactionType type,
        decimal net,
        decimal fee,
        BlockchainTransactionStatus status = BlockchainTransactionStatus.CONFIRMED,
        string? from = null)
    {
        var row = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Network = deposit.Network,
            Type = type,
            TxHash = status == BlockchainTransactionStatus.PENDING ? null : "refund-" + Guid.NewGuid().ToString("N"),
            FromAddress = from ?? deposit.Address,
            ToAddress = tx.BuyerRefundAddress!,
            Amount = net,
            GasFee = fee,
            Token = StablecoinType.USDT,
            ActualTokenAddress = type == BlockchainTransactionType.WRONG_TOKEN_REFUND ? TronUsdc : null,
            Status = status,
            ConfirmationCount = status == BlockchainTransactionStatus.CONFIRMED ? 20 : 0,
            ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED ? Now : null,
            CreatedAt = Now,
        };
        _db.Set<BlockchainTransaction>().Add(row);
        return row;
    }

    private void AddSweep(Transaction tx, PaymentAddress deposit, BlockchainTransactionStatus status, decimal? recovered) =>
        _db.Set<BlockchainTransaction>().Add(new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = deposit.Id,
            Network = deposit.Network,
            Type = BlockchainTransactionType.SWEEP,
            TxHash = status == BlockchainTransactionStatus.PENDING ? null : "sweep-" + Guid.NewGuid().ToString("N"),
            FromAddress = deposit.Address,
            ToAddress = deposit.Network == BlockchainNetwork.TRON ? TronHot : EthHot,
            Amount = tx.TotalAmount + (recovered ?? 0m),
            RefundFeeRecovered = recovered,
            Token = StablecoinType.USDT,
            Status = status,
            ConfirmationCount = status == BlockchainTransactionStatus.CONFIRMED ? 20 : 0,
            ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED ? Now : null,
            CreatedAt = Now,
        });

    private BlockchainTransaction AddCollection(
        Transaction tx, PaymentAddress deposit, decimal amount, BlockchainTransactionStatus status)
    {
        var row = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = deposit.Id,
            Network = deposit.Network,
            Type = BlockchainTransactionType.REFUND_FEE_SWEEP,
            TxHash = status == BlockchainTransactionStatus.PENDING ? null : "collect-" + Guid.NewGuid().ToString("N"),
            FromAddress = deposit.Address,
            ToAddress = TronHot,
            Amount = amount,
            RefundFeeRecovered = amount,
            Token = StablecoinType.USDT,
            Status = status,
            ConfirmationCount = status == BlockchainTransactionStatus.CONFIRMED ? 20 : 0,
            ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED ? Now : null,
            CreatedAt = Now,
        };
        _db.Set<BlockchainTransaction>().Add(row);
        return row;
    }

    private async Task ConfirmAsync(Guid id)
    {
        var row = await _db.Set<BlockchainTransaction>().FirstAsync(b => b.Id == id);
        row.Status = BlockchainTransactionStatus.CONFIRMED;
        row.TxHash = "collect-" + Guid.NewGuid().ToString("N");
        row.ConfirmationCount = 20;
        row.ConfirmedAt = Now;
        await _db.SaveChangesAsync();
    }

    private Task<BlockchainTransaction> SingleCollectionAsync(Transaction tx) =>
        _db.Set<BlockchainTransaction>().AsNoTracking()
            .SingleAsync(b => b.TransactionId == tx.Id && b.Type == BlockchainTransactionType.REFUND_FEE_SWEEP);

    private Task<bool> AnyCollectionAsync(Transaction tx) =>
        _db.Set<BlockchainTransaction>().AnyAsync(
            b => b.TransactionId == tx.Id && b.Type == BlockchainTransactionType.REFUND_FEE_SWEEP);

    private Task<int> CollectionCountAsync(Transaction tx) =>
        _db.Set<BlockchainTransaction>().CountAsync(
            b => b.TransactionId == tx.Id && b.Type == BlockchainTransactionType.REFUND_FEE_SWEEP);

    private async Task<DateTime?> NextCheckAsync(PaymentAddress deposit) =>
        (await _db.Set<PaymentAddress>().AsNoTracking().SingleAsync(p => p.Id == deposit.Id))
            .RefundFeeSweepNextCheckAt;

    private sealed class StubWalletAddresses : IPlatformWalletAddressProvider
    {
        public string? Tron { get; set; }
        public string? Ethereum { get; set; }

        public string? HotWalletAddress => Tron;
        public string? ColdWalletAddress => null;

        public PlatformWalletAddresses For(BlockchainNetwork network) => network == BlockchainNetwork.TRON
            ? new(network, Tron, null)
            : new(network, Ethereum, null);
    }

    private sealed class StubEstimator : IGasFeeEstimator
    {
        public decimal? Fee { get; set; }
        public bool Throw { get; set; }
        public List<GasFeeEstimateRequest> Requests { get; } = [];

        public Task<decimal?> EstimateFeeUsdtAsync(GasFeeEstimateRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Throw) throw new HttpRequestException("estimate-fee unreachable");
            return Task.FromResult(Fee);
        }

        public Task<SweepCostEstimate?> EstimateSweepCostAsync(StablecoinType token, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The collector prices the exact transfer, not a fresh-deposit sweep.");
    }

    private sealed class StubSettings : IBlockchainNetworkSettingsProvider
    {
        public decimal TronRatio { get; set; } = 2.0m;

        public Task<BlockchainNetworkSettings> GetAsync(BlockchainNetwork network, CancellationToken cancellationToken) =>
            Task.FromResult(network == BlockchainNetwork.TRON
                ? BlockchainNetworkSettingsProvider.TronDefaults with { MinRefundThresholdRatio = TronRatio }
                : BlockchainNetworkSettingsProvider.EthereumDefaults);
    }

    private sealed class StubContracts : IStablecoinContractRegistry
    {
        public string ResolveContractAddress(BlockchainNetwork network, StablecoinType token) =>
            (network, token) switch
            {
                (BlockchainNetwork.TRON, StablecoinType.USDC) => TronUsdc,
                (BlockchainNetwork.ETHEREUM, StablecoinType.USDC) => EthUsdc,
                _ => "usdt-" + network,
            };

        public StablecoinType? ResolveByContract(BlockchainNetwork network, string? contractAddress) =>
            contractAddress == TronUsdc || contractAddress == EthUsdc ? StablecoinType.USDC
            : contractAddress == "usdt-" + network ? StablecoinType.USDT
            : null;
    }
}
