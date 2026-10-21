using System.Globalization;
using Skinora.Transactions.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Platform.Application.Wallets;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Transfers;

/// <summary>
/// Unit coverage for <see cref="OutgoingTransferConfirmationJob"/> (T73).
/// Confirms DETECTED → CONFIRMED on finality, DETECTED → FAILED on contract
/// revert, and no-op on pending / unavailable.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OutgoingTransferConfirmationJobTests : IDisposable
{
    static OutgoingTransferConfirmationJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly StubBlockchainTransferClient _client;
    private readonly SettableNetworkHealth _health = new();
    private readonly StubWalletAddresses _walletAddresses = new();
    private readonly FakeBlockchainServiceRegistry _registry;
    private readonly CapturingOutboxService _outbox;
    private readonly FakeTimeProvider _clock;
    private readonly OutgoingTransferConfirmationJob _sut;
    private int _seedSequence;

    public OutgoingTransferConfirmationJobTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _client = new StubBlockchainTransferClient();
        _registry = FakeBlockchainServiceRegistry.ForTron(transfers: _client);
        _outbox = new CapturingOutboxService();
        _clock = new FakeTimeProvider();
        _clock.SetUtcNow(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        _sut = new OutgoingTransferConfirmationJob(
            _db,
            _registry,
            _health,
            _outbox,
            StablecoinContractRegistry.FromOptions(new StablecoinContractOptions()),
            _walletAddresses,
            _clock,
            NullLogger<OutgoingTransferConfirmationJob>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task ConfirmedStatus_FlipsRowToConfirmedAndStampsBlock()
    {
        var row = await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Confirmed, 1_500_020L, 25, "SUCCESS", null);

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == row.Id);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, reloaded.Status);
        Assert.Equal(1_500_020L, reloaded.BlockNumber);
        Assert.Equal(25, reloaded.ConfirmationCount);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, reloaded.ConfirmedAt);
    }

    // -------------------- T148 — the network's policy, not TRON's constants --------------------

    [Fact]
    public async Task Tron_Confirmation_Without_A_Count_Is_Stored_At_The_20_Block_Floor_With_Resource_Fields()
    {
        var row = await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Confirmed, 1_500_020L, null, "SUCCESS", null,
            RealizedFeeSun: 6_400_000L, EnergyUsageTotal: 64_285L, OriginEnergyUsage: 0L);

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == row.Id);
        Assert.Equal(20, reloaded.ConfirmationCount);
        Assert.Equal(6_400_000L, reloaded.RealizedFeeSun);
        Assert.Equal(64_285L, reloaded.EnergyUsageTotal);
    }

    [Fact]
    public async Task Ethereum_Confirmation_Is_Asked_Of_Ethereums_Service_And_Stores_No_Tron_Constants()
    {
        var ethereum = new StubBlockchainTransferClient
        {
            NextStatus = new TransferStatusResult(
                TransferStatusOutcome.Confirmed, 21_000_000L, null, "SUCCESS", null,
                RealizedFeeSun: 6_400_000L, EnergyUsageTotal: 64_285L, OriginEnergyUsage: 0L),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var row = await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT);
        row.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.Single(ethereum.StatusCalls);
        Assert.Empty(_client.StatusCalls);
        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == row.Id);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, reloaded.Status);
        // No synthetic 20, no SUN / Energy on an Ethereum row.
        Assert.Equal(0, reloaded.ConfirmationCount);
        Assert.Null(reloaded.RealizedFeeSun);
        Assert.Null(reloaded.EnergyUsageTotal);
        Assert.Null(reloaded.OriginEnergyUsage);
    }

    // -------------------- T158 (g) — unverifiable final transfer: fail closed --------------------

    [Fact]
    public async Task T158g_Unverified_Status_Leaves_The_Row_Detected_Never_Confirmed_Never_Failed()
    {
        var ethereum = new StubBlockchainTransferClient
        {
            NextStatus = new TransferStatusResult(
                TransferStatusOutcome.Unverified, 26_215_000L, 13, "UNKNOWN", null),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var row = await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT);
        row.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == row.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, reloaded.Status);
        Assert.Null(reloaded.ConfirmedAt);
        Assert.StartsWith("TRANSFER_UNVERIFIED", reloaded.ErrorMessage);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, reloaded.NextAttemptAt); // rotation stamp
        Assert.Empty(_outbox.Events); // no PayoutCompletedEvent
    }

    [Fact]
    public async Task T158g_Ethereum_Status_Reads_Carry_The_Row_Expectation_Payout_Names_The_Hot_Wallet()
    {
        const string hot = "0x7070707070707070707070707070707070707070";
        _walletAddresses.EthereumHotWalletAddress = hot;
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var payout = await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT);
        payout.Network = BlockchainNetwork.ETHEREUM;
        payout.FromAddress = string.Empty; // a payout never records its sender
        var refund = await SeedDetectedAsync(BlockchainTransactionType.BUYER_REFUND);
        refund.Network = BlockchainNetwork.ETHEREUM;
        refund.FromAddress = "0x9858effd232b4033e47d90003d41ec34ecaeda94";
        var wrong = await SeedDetectedAsync(BlockchainTransactionType.WRONG_TOKEN_REFUND);
        wrong.Network = BlockchainNetwork.ETHEREUM;
        wrong.ActualTokenAddress = "0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48";
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var byHash = ethereum.StatusCalls.Zip(ethereum.Expectations).ToDictionary(p => p.First, p => p.Second);
        var usdt = StablecoinContractRegistry.FromOptions(new StablecoinContractOptions())
            .ResolveContractAddress(BlockchainNetwork.ETHEREUM, StablecoinType.USDT);
        Assert.Equal(new TransferStatusExpectation(usdt, hot, payout.ToAddress, payout.Amount), byHash[payout.TxHash!]);
        Assert.Equal(refund.FromAddress, byHash[refund.TxHash!]!.FromAddress);
        Assert.Equal("0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48", byHash[wrong.TxHash!]!.Contract);
    }

    [Fact]
    public async Task T158g_Without_A_Known_Sender_No_Expectation_Is_Sent_And_Tron_Never_Sends_One()
    {
        _walletAddresses.EthereumHotWalletAddress = null;
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var payout = await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT);
        payout.Network = BlockchainNetwork.ETHEREUM;
        payout.FromAddress = string.Empty;
        await _db.SaveChangesAsync();
        await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT); // TRON

        await _sut.ExecuteAsync();

        Assert.Null(Assert.Single(ethereum.Expectations));
        Assert.All(_client.Expectations, Assert.Null);
    }

    // -------------------- T154 — Ethereum's realized fee is wei, exact --------------------

    [Fact]
    public async Task Ethereum_Confirmation_Records_The_Realized_Fee_In_Wei_Exactly()
    {
        // 9,007,282,507,774,707 wei — above 2^53, so a double would store …708.
        var ethereum = new StubBlockchainTransferClient
        {
            NextStatus = new TransferStatusResult(
                TransferStatusOutcome.Confirmed, 21_000_000L, null, "SUCCESS", null,
                RealizedFeeWei: 9_007_282_507_774_707m),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var row = await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT);
        row.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == row.Id);
        Assert.Equal(9_007_282_507_774_707m, reloaded.RealizedFeeWei);
        Assert.Null(reloaded.RealizedFeeSun);
    }

    // -------------------- T160c düzeltme turu — platform spend valued at confirmation --------------------

    [Fact]
    public async Task T160c_Ethereum_Confirmation_Records_The_Platform_Spend_And_Values_It_At_The_Reported_Price()
    {
        // T159b senaryo 2 (Sepolia): fuel 168,966,002,534,490 wei + 204,600 gas at 1 gwei;
        // the refund's own receipt 152,417 gas at 1 gwei; ETH 2,444.861904 USDT.
        var ethereum = new StubBlockchainTransferClient
        {
            NextStatus = new TransferStatusResult(
                TransferStatusOutcome.Confirmed, 11_959_101L, null, "SUCCESS", null,
                RealizedFeeWei: 152_417_000_000_000m,
                PlatformSpendWei: 373_566_002_534_490m,
                NativePriceUsdt: 2444.861904m),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var row = await SeedDetectedAsync(BlockchainTransactionType.BUYER_REFUND);
        row.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == row.Id);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, reloaded.Status);
        Assert.Equal(152_417_000_000_000m, reloaded.RealizedFeeWei);
        Assert.Equal(373_566_002_534_490m, reloaded.PlatformNetworkCost);
        Assert.Equal(0.913317m, reloaded.PlatformNetworkCostUsdt); // 0.000373566002534490 × 2,444.861904
    }

    [Fact]
    public async Task T160c_Tron_Confirmation_Values_The_Spend_The_Broadcast_Recorded_And_Keeps_It()
    {
        var row = await SeedDetectedAsync(BlockchainTransactionType.INCORRECT_AMOUNT_REFUND);
        row.PlatformNetworkCost = 2_711_500m;
        await _db.SaveChangesAsync();
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Confirmed, 72_004_108L, 20, "SUCCESS", null,
            RealizedFeeSun: 0L, PlatformSpendWei: 9m, NativePriceUsdt: 0.3313m);

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == row.Id);
        Assert.Equal(2_711_500m, reloaded.PlatformNetworkCost); // a wei figure never lands on TRON
        Assert.Equal(0.898320m, reloaded.PlatformNetworkCostUsdt);
        Assert.Equal(0L, reloaded.RealizedFeeSun);
    }

    [Fact]
    public async Task T160c_Without_A_Price_The_Spend_Is_Kept_And_Left_Unvalued()
    {
        var ethereum = new StubBlockchainTransferClient
        {
            NextStatus = new TransferStatusResult(
                TransferStatusOutcome.Confirmed, 11_959_101L, null, "SUCCESS", null,
                RealizedFeeWei: 152_393_000_000_000m, PlatformSpendWei: 373_540_002_534_100m),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var row = await SeedDetectedAsync(BlockchainTransactionType.LATE_PAYMENT_REFUND);
        row.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == row.Id);
        Assert.Equal(373_540_002_534_100m, reloaded.PlatformNetworkCost);
        Assert.Null(reloaded.PlatformNetworkCostUsdt);
    }

    [Fact]
    public async Task T160c_An_Unrealistic_Price_Leaves_The_Spend_Unvalued_And_Still_Confirms()
    {
        // S4-4 — 0.37354 ETH at 10^13 USDT/ETH is 3.7e12 USDT: more than
        // decimal(18,6) holds. The row is confirmed with its spend, unvalued.
        var ethereum = new StubBlockchainTransferClient
        {
            NextStatus = new TransferStatusResult(
                TransferStatusOutcome.Confirmed, 11_959_101L, null, "SUCCESS", null,
                RealizedFeeWei: 152_393_000_000_000m, PlatformSpendWei: 373_540_002_534_100_000m,
                NativePriceUsdt: 10_000_000_000_000m),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var row = await SeedDetectedAsync(BlockchainTransactionType.LATE_PAYMENT_REFUND);
        row.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == row.Id);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, reloaded.Status);
        Assert.Equal(373_540_002_534_100_000m, reloaded.PlatformNetworkCost);
        Assert.Null(reloaded.PlatformNetworkCostUsdt);
    }

    [Theory]
    [InlineData("999999999999999999999999999", "79228162514264337593543950335")] // decimal overflow
    [InlineData("1000000000000000000", "1000000000000")]                          // 10^12: one above the column
    public void T160c_PlatformCostUsdt_Is_Null_Where_The_Column_Cannot_Hold_It(string wei, string price)
    {
        var policy = BlockchainNetworkPolicy.For(BlockchainNetwork.ETHEREUM);
        Assert.Null(OutgoingTransferConfirmationJob.PlatformCostUsdt(
            decimal.Parse(wei, CultureInfo.InvariantCulture), decimal.Parse(price, CultureInfo.InvariantCulture), policy));
        // The column's own maximum is still a value.
        Assert.Equal(
            OutgoingTransferConfirmationJob.MaxPlatformCostUsdt,
            OutgoingTransferConfirmationJob.PlatformCostUsdt(1_000_000_000_000_000_000m,
                OutgoingTransferConfirmationJob.MaxPlatformCostUsdt, policy));
    }

    [Fact]
    public async Task Tron_Confirmation_Never_Records_A_Wei_Fee()
    {
        var row = await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Confirmed, 1_500_020L, null, "SUCCESS", null,
            RealizedFeeSun: 6_400_000L, RealizedFeeWei: 123m);

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == row.Id);
        Assert.Null(reloaded.RealizedFeeWei);
        Assert.Equal(6_400_000L, reloaded.RealizedFeeSun);
    }

    [Fact]
    public async Task FailedStatus_FlipsRowToFailed_WithContractRetMessage()
    {
        var row = await SeedDetectedAsync(BlockchainTransactionType.BUYER_REFUND);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Failed, 1_500_021L, 25, "REVERT", null);

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == row.Id);
        Assert.Equal(BlockchainTransactionStatus.FAILED, reloaded.Status);
        Assert.Equal(1_500_021L, reloaded.BlockNumber);
        Assert.Contains("REVERT", reloaded.ErrorMessage);
    }

    [Fact]
    public async Task PendingStatus_LeavesRowUnchanged()
    {
        var row = await SeedDetectedAsync(BlockchainTransactionType.EXCESS_REFUND);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Pending, null, 5, null, null);

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == row.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, reloaded.Status);
        Assert.Null(reloaded.ConfirmedAt);
    }

    [Fact]
    public async Task UnavailableStatus_LeavesRowUnchanged()
    {
        var row = await SeedDetectedAsync(BlockchainTransactionType.WRONG_TOKEN_REFUND);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Unavailable, null, null, null, "503");

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == row.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, reloaded.Status);
    }

    // T148 düzeltme turu 1 (F1) — a DETECTED row on a network whose service is
    // gone is answered Unavailable and stays DETECTED; selected oldest-first it
    // would hold every slot of the batch on every run. The TRON row behind
    // BatchSize (and more) such rows must be confirmed on the first run.
    [Theory]
    [InlineData(OutgoingTransferConfirmationJob.BatchSize)]
    [InlineData(OutgoingTransferConfirmationJob.BatchSize + 5)]
    public async Task Detected_Rows_On_An_Unconfigured_Network_Do_Not_Starve_Tron(int parkedRows)
    {
        for (var i = 0; i < parkedRows; i++)
        {
            var eth = await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT);
            eth.Network = BlockchainNetwork.ETHEREUM;
            _clock.Advance(TimeSpan.FromSeconds(1));
        }
        await _db.SaveChangesAsync();

        var tron = await SeedDetectedAsync(BlockchainTransactionType.BUYER_REFUND);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Confirmed, 1_500_000L, 20, "SUCCESS", null);

        await _sut.ExecuteAsync();

        Assert.Equal(tron.TxHash, Assert.Single(_client.StatusCalls));
        Assert.DoesNotContain(_registry.Requests, r => r.Network == BlockchainNetwork.ETHEREUM);
        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == tron.Id);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, reloaded.Status);
        Assert.Equal(
            parkedRows,
            await _db.Set<BlockchainTransaction>().AsNoTracking().CountAsync(b =>
                b.Network == BlockchainNetwork.ETHEREUM && b.Status == BlockchainTransactionStatus.DETECTED));
    }

    // -------------------- T150 — configured but unreachable network --------------------

    private async Task<List<BlockchainTransaction>> SeedEthereumDetectedAsync(int count)
    {
        var rows = new List<BlockchainTransaction>();
        for (var i = 0; i < count; i++)
        {
            var eth = await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT);
            eth.Network = BlockchainNetwork.ETHEREUM;
            rows.Add(eth);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }
        await _db.SaveChangesAsync();
        return rows;
    }

    [Fact]
    public async Task A_Down_Network_Is_Asked_Once_Per_Run_And_Does_Not_Hold_Tron()
    {
        var ethereum = new StubBlockchainTransferClient
        {
            NextStatus = new TransferStatusResult(TransferStatusOutcome.Unavailable, null, null, null, "connection refused"),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var ethRows = await SeedEthereumDetectedAsync(OutgoingTransferConfirmationJob.BatchSize + 5);
        var tron = await SeedDetectedAsync(BlockchainTransactionType.BUYER_REFUND);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Confirmed, 1_500_000L, 20, "SUCCESS", null);

        await _sut.ExecuteAsync();

        Assert.Equal(tron.TxHash, Assert.Single(_client.StatusCalls));
        Assert.Equal(ethRows[0].TxHash, Assert.Single(ethereum.StatusCalls));
        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == tron.Id);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, reloaded.Status);
        Assert.Equal(
            ethRows.Count,
            await _db.Set<BlockchainTransaction>().AsNoTracking().CountAsync(b =>
                b.Network == BlockchainNetwork.ETHEREUM && b.Status == BlockchainTransactionStatus.DETECTED));
    }

    [Fact]
    public async Task Each_Network_Has_Its_Own_Batch_Of_Pending_Confirmations()
    {
        // Ethereum rows waiting for `finalized` (Pending) fill a whole batch and
        // are all older than TRON's; TRON's are still read in the same run,
        // round-robin.
        var ethereum = new StubBlockchainTransferClient
        {
            NextStatus = new TransferStatusResult(TransferStatusOutcome.Pending, null, null, null, null),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        await SeedEthereumDetectedAsync(OutgoingTransferConfirmationJob.BatchSize);
        var tron = await SeedDetectedAsync(BlockchainTransactionType.BUYER_REFUND);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Confirmed, 1_500_000L, 20, "SUCCESS", null);

        await _sut.ExecuteAsync();

        Assert.Equal(tron.TxHash, Assert.Single(_client.StatusCalls));
        Assert.Equal(OutgoingTransferConfirmationJob.BatchSize, ethereum.StatusCalls.Count);
        Assert.Equal(
            BlockchainNetwork.TRON,
            _registry.Requests.First(r => r.Kind == "Transfers").Network);
    }

    [Fact]
    public async Task A_Network_In_A_Detected_Outage_Is_Not_Asked()
    {
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        await SeedEthereumDetectedAsync(2);
        var tron = await SeedDetectedAsync(BlockchainTransactionType.BUYER_REFUND);
        _health.Degraded.Add(BlockchainNetwork.ETHEREUM);

        await _sut.ExecuteAsync();

        Assert.Empty(ethereum.StatusCalls);
        Assert.DoesNotContain(_registry.Requests, r => r.Network == BlockchainNetwork.ETHEREUM);
        Assert.Equal(tron.TxHash, Assert.Single(_client.StatusCalls));
    }

    // -------------------- T152 — rotation and the row-specific 4xx --------------------

    [Fact]
    public async Task A_Row_Specific_4xx_On_The_Oldest_Row_Does_Not_Stop_The_Networks_Other_Rows()
    {
        // T150 doğrulaması: the oldest DETECTED row keeps getting 400/404/409
        // (Rejected); the network's other rows are confirmed in the SAME run.
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var rows = await SeedEthereumDetectedAsync(4);
        ethereum.Respond = hash => hash == rows[0].TxHash
            ? new TransferStatusResult(TransferStatusOutcome.Rejected, null, null, null, "404 TRANSFER_NOT_FOUND")
            : new TransferStatusResult(TransferStatusOutcome.Confirmed, 21_000_000L, 3, "SUCCESS", null);

        await _sut.ExecuteAsync();

        Assert.Equal(rows.Select(r => r.TxHash), ethereum.StatusCalls);
        var states = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .Where(b => b.Network == BlockchainNetwork.ETHEREUM)
            .ToDictionaryAsync(b => b.Id, b => b.Status);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, states[rows[0].Id]);
        Assert.All(rows.Skip(1), r => Assert.Equal(BlockchainTransactionStatus.CONFIRMED, states[r.Id]));
    }

    [Fact]
    public async Task An_Unavailable_Answer_Still_Ends_The_Networks_Turn()
    {
        var ethereum = new StubBlockchainTransferClient
        {
            NextStatus = new TransferStatusResult(TransferStatusOutcome.Unavailable, null, null, null, "502"),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        await SeedEthereumDetectedAsync(3);

        await _sut.ExecuteAsync();

        Assert.Single(ethereum.StatusCalls);
    }

    [Fact]
    public async Task Long_Pending_Rows_Take_Turns_So_A_Newer_Row_Behind_A_Full_Batch_Is_Read()
    {
        // BatchSize + 5 Ethereum rows waiting for finalized. Oldest-first alone
        // would read the same 30 forever; with rotation the 5 newest are read on
        // the second run.
        var ethereum = new StubBlockchainTransferClient
        {
            NextStatus = new TransferStatusResult(TransferStatusOutcome.Pending, null, null, null, null),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var rows = await SeedEthereumDetectedAsync(OutgoingTransferConfirmationJob.BatchSize + 5);

        await _sut.ExecuteAsync();
        Assert.Equal(rows.Take(OutgoingTransferConfirmationJob.BatchSize).Select(r => r.TxHash), ethereum.StatusCalls);

        ethereum.StatusCalls.Clear();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _sut.ExecuteAsync();

        var newest = rows.Skip(OutgoingTransferConfirmationJob.BatchSize).Select(r => r.TxHash).ToList();
        Assert.Equal(newest, ethereum.StatusCalls.Take(newest.Count));
        Assert.Equal(OutgoingTransferConfirmationJob.BatchSize, ethereum.StatusCalls.Count);
    }

    [Fact]
    public async Task Rows_Refused_With_A_Row_Specific_4xx_Also_Take_Turns()
    {
        // T152 düzeltme turu 2 (B10): BatchSize + 5 Ethereum rows whose status
        // keeps answering a row-specific 4xx (404 / 409 — Rejected). Without the
        // rotation stamp on Rejected the same 30 would hold the batch forever
        // and the 5 newest would never be read.
        var ethereum = new StubBlockchainTransferClient
        {
            NextStatus = new TransferStatusResult(TransferStatusOutcome.Rejected, null, null, null, "409 TRANSFER_NONCE_CONFLICT"),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var rows = await SeedEthereumDetectedAsync(OutgoingTransferConfirmationJob.BatchSize + 5);

        await _sut.ExecuteAsync();
        Assert.Equal(rows.Take(OutgoingTransferConfirmationJob.BatchSize).Select(r => r.TxHash), ethereum.StatusCalls);

        ethereum.StatusCalls.Clear();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _sut.ExecuteAsync();

        var newest = rows.Skip(OutgoingTransferConfirmationJob.BatchSize).Select(r => r.TxHash).ToList();
        Assert.Equal(newest, ethereum.StatusCalls.Take(newest.Count));
        var stamped = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .Where(b => b.Network == BlockchainNetwork.ETHEREUM && b.NextAttemptAt != null)
            .CountAsync();
        Assert.Equal(rows.Count, stamped);
    }

    [Fact]
    public async Task A_Same_Nonce_Replacement_Is_Recorded_With_The_Hash_That_Is_On_Chain()
    {
        var row = await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT);
        var onChain = "0x" + new string('A', 64);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Confirmed, 1_500_050L, 22, "SUCCESS", null, TxHash: onChain);

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == row.Id);
        Assert.Equal(onChain.ToLowerInvariant(), reloaded.TxHash);
        Assert.Equal(onChain.ToLowerInvariant(), Assert.Single(_outbox.Events.OfType<PayoutCompletedEvent>()).PayoutTxHash);
    }

    [Fact]
    public async Task InboundDetectedRow_IsNotPolled()
    {
        var row = await SeedDetectedAsync(BlockchainTransactionType.BUYER_PAYMENT);

        await _sut.ExecuteAsync();

        Assert.Empty(_client.StatusCalls);
        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == row.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, reloaded.Status);
    }

    [Fact]
    public async Task SellerPayoutConfirmed_PublishesPayoutCompletedEvent()
    {
        var row = await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Confirmed, 1_500_030L, 22, "SUCCESS", null);

        await _sut.ExecuteAsync();

        var evt = Assert.Single(_outbox.Events.OfType<PayoutCompletedEvent>());
        Assert.Equal(row.TransactionId, evt.TransactionId);
        Assert.Equal(row.TxHash, evt.PayoutTxHash);
        Assert.Equal(row.Amount, evt.NetAmount);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, evt.OccurredAt);
    }

    [Fact]
    public async Task RefundConfirmed_DoesNotPublishPayoutCompletedEvent()
    {
        await SeedDetectedAsync(BlockchainTransactionType.BUYER_REFUND);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Confirmed, 1_500_031L, 22, "SUCCESS", null);

        await _sut.ExecuteAsync();

        Assert.Empty(_outbox.Events.OfType<PayoutCompletedEvent>());
    }

    [Fact]
    public async Task SweepConfirmed_FlipsToConfirmed_AndDoesNotPublishPayoutCompletedEvent()
    {
        // WP3 — a broadcast SWEEP must be driven DETECTED → CONFIRMED here (it is
        // now in OutboundTypes) so reconciliation's CONFIRMED-only filter credits
        // the hot wallet inflow. SWEEP drives no transaction-state transition, so
        // it must NOT emit PayoutCompletedEvent.
        var row = await SeedDetectedAsync(BlockchainTransactionType.SWEEP);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Confirmed, 1_500_040L, 22, "SUCCESS", null);

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == row.Id);
        Assert.Equal(BlockchainTransactionStatus.CONFIRMED, reloaded.Status);
        Assert.Empty(_outbox.Events.OfType<PayoutCompletedEvent>());
    }

    [Fact]
    public async Task SellerPayoutFailed_DoesNotPublishPayoutCompletedEvent()
    {
        await SeedDetectedAsync(BlockchainTransactionType.SELLER_PAYOUT);
        _client.NextStatus = new TransferStatusResult(
            TransferStatusOutcome.Failed, 1_500_032L, 22, "REVERT", null);

        await _sut.ExecuteAsync();

        Assert.Empty(_outbox.Events.OfType<PayoutCompletedEvent>());
    }

    private async Task<BlockchainTransaction> SeedDetectedAsync(BlockchainTransactionType type)
    {
        // Unique per call (SteamId and (Network, HdWalletIndex) are unique) so a
        // test can seed a whole batch.
        var seq = ++_seedSequence;
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = $"7656119800{seq * 2:D7}",
            SteamDisplayName = "Seller",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = $"7656119800{(seq * 2) + 1:D7}",
            SteamDisplayName = "Buyer",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<User>().AddRange(seller, buyer);

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.PAYMENT_RECEIVED,
            SellerId = seller.Id,
            BuyerId = buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000913",
            BuyerRefundAddress = "TBuyerRefund000000000000000000000000",
            ItemAssetId = "asset-1",
            ItemClassId = "cls",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 98m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 100m,
            SellerPayoutAddress = "TSellerPayout00000000000000000000000",
        };

        // BUYER_PAYMENT needs a PaymentAddress (CK_*_BuyerPayment); SWEEP is
        // deposit-anchored too (CK_*_Type_Sweep requires PaymentAddressId NOT NULL).
        Guid? paymentAddressId = null;
        if (type == BlockchainTransactionType.BUYER_PAYMENT
            || type == BlockchainTransactionType.SWEEP)
        {
            var pa = new PaymentAddress
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                Address = "TDeposit" + Guid.NewGuid().ToString("N").Substring(0, 26),
                HdWalletIndex = seq,
                ExpectedAmount = 100m,
                ExpectedToken = StablecoinType.USDT,
                MonitoringStatus = MonitoringStatus.ACTIVE,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
            };
            _db.Set<PaymentAddress>().Add(pa);
            paymentAddressId = pa.Id;
        }

        var row = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = paymentAddressId,
            Type = type,
            TxHash = "0xDetected" + Guid.NewGuid().ToString("N"),
            FromAddress = "TFrom00000000000000000000000000000000",
            ToAddress = "TTo0000000000000000000000000000000000",
            Amount = 50m,
            Token = StablecoinType.USDT,
            // CK_BlockchainTransactions_Type_WrongTokenRefund requires NOT NULL.
            ActualTokenAddress = type == BlockchainTransactionType.WRONG_TOKEN_REFUND
                ? "TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8"
                : null,
            Status = BlockchainTransactionStatus.DETECTED,
            ConfirmationCount = 0,
            RetryCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<Transaction>().Add(tx);
        _db.Set<BlockchainTransaction>().Add(row);
        await _db.SaveChangesAsync();

        return row;
    }

    /// <summary>T158 (g) — the hot wallet a payout's expectation names as sender.</summary>
    private sealed class StubWalletAddresses : IPlatformWalletAddressProvider
    {
        public string? HotWalletAddress { get; set; }
        public string? ColdWalletAddress { get; set; }
        public string? EthereumHotWalletAddress { get; set; }

        public PlatformWalletAddresses For(BlockchainNetwork network) => network switch
        {
            BlockchainNetwork.TRON => new(network, HotWalletAddress, ColdWalletAddress),
            _ => new(network, EthereumHotWalletAddress, null),
        };
    }

    private sealed class StubBlockchainTransferClient : IBlockchainTransferClient
    {
        public TransferStatusResult NextStatus { get; set; } =
            new(TransferStatusOutcome.Pending, null, 0, null, null);

        public List<string> StatusCalls { get; } = new();

        /// <summary>Per-hash answer (T152 tests); falls back to <see cref="NextStatus"/>.</summary>
        public Func<string, TransferStatusResult?>? Respond { get; set; }

        public Task<TransferBroadcastResult> BroadcastAsync(
            TransferBroadcastRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Confirmation job should not call Broadcast.");

        public Task<TransferStatusResult> GetStatusAsync(
            string txHash, CancellationToken cancellationToken) =>
            GetStatusAsync(txHash, null, cancellationToken);

        /// <summary>T158 (g) — the expectation each status read carried.</summary>
        public List<TransferStatusExpectation?> Expectations { get; } = new();

        public Task<TransferStatusResult> GetStatusAsync(
            string txHash, TransferStatusExpectation? expected, CancellationToken cancellationToken)
        {
            StatusCalls.Add(txHash);
            Expectations.Add(expected);
            return Task.FromResult(Respond?.Invoke(txHash) ?? NextStatus);
        }
    }

    private sealed class CapturingOutboxService : IOutboxService
    {
        public List<IDomainEvent> Events { get; } = new();

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
