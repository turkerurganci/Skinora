using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;
using Skinora.Transactions.Application.Webhooks;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Transfers;

/// <summary>
/// Unit coverage for <see cref="OutgoingTransferDispatchJob"/> (T73 — 08 §3.3,
/// 05 §3.3). Exercises every dispatcher outcome (success, transient retry,
/// terminal failure, invalid request) against a SQLite in-memory DbContext
/// with a stub <see cref="IBlockchainTransferClient"/>. Assertions focus on
/// the persistence contract (Status flip, TxHash, RetryCount, NextAttemptAt)
/// and the outbox event shape on FAILED.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OutgoingTransferDispatchJobTests : IDisposable
{
    static OutgoingTransferDispatchJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly StubBlockchainTransferClient _client;
    private readonly FakeBlockchainServiceRegistry _registry;
    private readonly SettableNetworkHealth _health = new();
    private readonly StubRetryPolicy _retryPolicy;
    private readonly CapturingOutboxService _outbox;
    private readonly FakeTimeProvider _clock;
    private readonly OutgoingTransferDispatchJob _sut;
    private int _seedSequence;

    public OutgoingTransferDispatchJobTests()
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
        _retryPolicy = new StubRetryPolicy();
        _outbox = new CapturingOutboxService();
        _clock = new FakeTimeProvider();
        _clock.SetUtcNow(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        // Empty options resolve to the mainnet constants (StablecoinContractOptions),
        // which is what SeedRefundAsync's ActualTokenAddress fixture matches.
        _sut = new OutgoingTransferDispatchJob(
            _db,
            _registry,
            _health,
            _retryPolicy,
            _outbox,
            StablecoinContractRegistry.FromOptions(new StablecoinContractOptions()),
            _clock,
            NullLogger<OutgoingTransferDispatchJob>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task SuccessfulBroadcast_FlipsRowToDetected_AndStampsTxHash()
    {
        var fixture = await SeedRefundAsync(BlockchainTransactionType.INCORRECT_AMOUNT_REFUND, amount: 50m);
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.Success, "tx-hash-001", null, null);

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, reloaded.Status);
        Assert.Equal("tx-hash-001", reloaded.TxHash);
        Assert.Equal(fixture.PaymentAddress.Address, reloaded.FromAddress);
        Assert.Equal(0, reloaded.RetryCount);
        Assert.Null(reloaded.NextAttemptAt);
        Assert.Empty(_outbox.Events.OfType<TransferDispatchFailedEvent>());
    }

    [Fact]
    public async Task SellerPayout_DoesNotRequireDepositAddress_BroadcastsFromHotWallet()
    {
        var fixture = await SeedPayoutAsync(amount: 100m);
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.Success, "tx-hash-payout", null, null);

        await _sut.ExecuteAsync();

        Assert.Single(_client.Calls);
        var call = _client.Calls[0];
        Assert.Equal(BlockchainTransactionType.SELLER_PAYOUT, call.Type);
        Assert.Null(call.DepositIndex);
        Assert.Null(call.DepositAddress);

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.PayoutRow.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, reloaded.Status);
        Assert.Equal("tx-hash-payout", reloaded.TxHash);
    }

    // -------------------- T148 — same-network rule, fail closed per network --------------------

    [Fact]
    public async Task Row_On_An_Unconfigured_Network_Stays_Pending_Untouched_And_Nothing_Is_Sent()
    {
        // Until T150 Ethereum has no service. A row on it must not be sent to
        // TRON's service, must not burn its retry budget and must not flip to
        // FAILED — it waits, PENDING, for its own network's service.
        var fixture = await SeedPayoutAsync(amount: 100m);
        fixture.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.Empty(_client.Calls);
        Assert.DoesNotContain(_registry.Requests, r => r.Kind == "Transfers");
        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.PayoutRow.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, reloaded.Status);
        Assert.Equal(0, reloaded.RetryCount);
        Assert.Null(reloaded.NextAttemptAt);
        Assert.Empty(_outbox.Events.OfType<TransferDispatchFailedEvent>());
    }

    [Fact]
    public async Task Each_Row_Is_Broadcast_By_Its_Own_Networks_Service()
    {
        var ethereumClient = new StubBlockchainTransferClient
        {
            NextResult = new TransferBroadcastResult(TransferBroadcastStatus.Success, "0xeth", null, null),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereumClient);

        var ethPayout = await SeedPayoutAsync(amount: 20m);
        ethPayout.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        // Same-network rule (11 §F8 K3): Ethereum's service, never TRON's.
        Assert.Equal(20m, Assert.Single(ethereumClient.Calls).Amount);
        Assert.Empty(_client.Calls);
        Assert.Equal(("Transfers", BlockchainNetwork.ETHEREUM), Assert.Single(_registry.Requests, r => r.Kind == "Transfers"));
        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == ethPayout.PayoutRow.Id);
        Assert.Equal("0xeth", reloaded.TxHash);
    }

    // T148 düzeltme turu 1 (F1). Parked rows (unconfigured network) are never
    // sent and keep NextAttemptAt null, so if the oldest-first batch could
    // select them they would take every slot on every run. Measured in round-1
    // validation: 20 parked Ethereum rows → TRON's client was never called;
    // 19 → it was. The newer TRON row must go out on the FIRST run, at exactly
    // BatchSize parked rows and above it.
    [Theory]
    [InlineData(OutgoingTransferDispatchJob.BatchSize)]
    [InlineData(OutgoingTransferDispatchJob.BatchSize + 5)]
    public async Task Parked_Rows_On_An_Unconfigured_Network_Do_Not_Starve_Tron(int parkedRows)
    {
        var parked = new List<Guid>();
        for (var i = 0; i < parkedRows; i++)
        {
            var eth = await SeedPayoutAsync(amount: 1m + i);
            eth.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
            parked.Add(eth.PayoutRow.Id);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }
        await _db.SaveChangesAsync();

        var tron = await SeedRefundAsync(BlockchainTransactionType.BUYER_REFUND, amount: 77m);
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.Success, "tx-hash-tron-behind-parked", null, null);

        await _sut.ExecuteAsync();

        var call = Assert.Single(_client.Calls);
        Assert.Equal(tron.RefundRow.Id, call.BlockchainTransactionId);
        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == tron.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, reloaded.Status);
        Assert.Equal("tx-hash-tron-behind-parked", reloaded.TxHash);

        // The parked rows are still untouched: PENDING, no retry spent, never
        // asked of any service.
        Assert.DoesNotContain(_registry.Requests, r => r.Network == BlockchainNetwork.ETHEREUM);
        var parkedRowsAfter = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .Where(b => parked.Contains(b.Id))
            .ToListAsync();
        Assert.Equal(parkedRows, parkedRowsAfter.Count);
        Assert.All(parkedRowsAfter, r =>
        {
            Assert.Equal(BlockchainTransactionStatus.PENDING, r.Status);
            Assert.Equal(0, r.RetryCount);
            Assert.Null(r.NextAttemptAt);
        });
    }

    [Fact]
    public async Task A_Full_Tron_Batch_Is_Still_Taken_Oldest_First_Behind_Parked_Rows()
    {
        // Ordering and batching are unchanged for the networks that can move:
        // BatchSize + 1 TRON rows → the oldest BatchSize go out, the newest waits.
        for (var i = 0; i < OutgoingTransferDispatchJob.BatchSize; i++)
        {
            var eth = await SeedPayoutAsync(amount: 1m);
            eth.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
            _clock.Advance(TimeSpan.FromSeconds(1));
        }
        await _db.SaveChangesAsync();

        var tronRows = new List<Guid>();
        for (var i = 0; i <= OutgoingTransferDispatchJob.BatchSize; i++)
        {
            tronRows.Add((await SeedRefundAsync(BlockchainTransactionType.EXCESS_REFUND, amount: 1m + i)).RefundRow.Id);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        await _sut.ExecuteAsync();

        Assert.Equal(tronRows.Take(OutgoingTransferDispatchJob.BatchSize), _client.Calls.Select(c => c.BlockchainTransactionId));
    }

    // -------------------- T150 — configured but unreachable network --------------------
    //
    // 11 §F8 T150 "Yapılandırılmış ama erişilemeyen ağ ortak kuyrukları
    // tıkamaz" (T148 validation N1–N2). T148 kept UNCONFIGURED networks out of
    // the batch; a configured network whose service is down still shared one
    // oldest-first batch with TRON and cost a timeout per row.

    private StubBlockchainTransferClient DownEthereum()
    {
        var ethereum = new StubBlockchainTransferClient
        {
            NextResult = new TransferBroadcastResult(
                TransferBroadcastStatus.TransientFailure, null,
                HttpBlockchainTransferClient.TransportFailureCode, "connection refused"),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        return ethereum;
    }

    private async Task<List<Guid>> SeedEthereumPayoutsAsync(int count)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var eth = await SeedPayoutAsync(amount: 1m + i);
            eth.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
            ids.Add(eth.PayoutRow.Id);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }
        await _db.SaveChangesAsync();
        return ids;
    }

    [Fact]
    public async Task A_Down_Network_Costs_One_Attempt_Per_Run_And_Does_Not_Hold_Tron()
    {
        var ethereum = DownEthereum();
        var ethRows = await SeedEthereumPayoutsAsync(OutgoingTransferDispatchJob.BatchSize + 5);
        var tron = await SeedRefundAsync(BlockchainTransactionType.BUYER_REFUND, amount: 77m);
        _client.NextResult = new TransferBroadcastResult(TransferBroadcastStatus.Success, "tx-tron", null, null);

        await _sut.ExecuteAsync();

        // TRON went out in the same run.
        Assert.Equal(tron.RefundRow.Id, Assert.Single(_client.Calls).BlockchainTransactionId);
        // Ethereum's service was asked once; the first unreachable answer
        // ended Ethereum's turn for the run.
        var attempted = Assert.Single(ethereum.Calls).BlockchainTransactionId;
        Assert.Equal(ethRows[0], attempted);

        var rows = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .Where(b => ethRows.Contains(b.Id)).ToListAsync();
        // Only the attempted row spent a retry (normal transient backoff); the
        // others are untouched.
        Assert.Equal(1, rows.Single(r => r.Id == attempted).RetryCount);
        Assert.All(rows.Where(r => r.Id != attempted), r =>
        {
            Assert.Equal(BlockchainTransactionStatus.PENDING, r.Status);
            Assert.Equal(0, r.RetryCount);
            Assert.Null(r.NextAttemptAt);
        });
    }

    [Fact]
    public async Task Each_Network_Has_Its_Own_Batch_And_They_Are_Worked_Round_Robin()
    {
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        // BatchSize Ethereum rows, all older than TRON's.
        await SeedEthereumPayoutsAsync(OutgoingTransferDispatchJob.BatchSize);
        var tronRows = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            tronRows.Add((await SeedRefundAsync(BlockchainTransactionType.EXCESS_REFUND, amount: 1m + i)).RefundRow.Id);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        await _sut.ExecuteAsync();

        // Before T150 one oldest-first batch of BatchSize was all Ethereum.
        Assert.Equal(tronRows, _client.Calls.Select(c => c.BlockchainTransactionId));
        Assert.Equal(OutgoingTransferDispatchJob.BatchSize, ethereum.Calls.Count);
        // Round-robin: the first TRON row goes out second, not after Ethereum's batch.
        var order = _registry.Requests.Where(r => r.Kind == "Transfers").Select(r => r.Network).Take(4).ToList();
        Assert.Equal(
            [BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM, BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM],
            order);
    }

    [Fact]
    public async Task A_Network_In_A_Detected_Outage_Is_Skipped_With_Its_Retry_Budget_Kept()
    {
        var ethereum = DownEthereum();
        var ethRows = await SeedEthereumPayoutsAsync(3);
        var tron = await SeedRefundAsync(BlockchainTransactionType.BUYER_REFUND, amount: 5m);
        _health.Degraded.Add(BlockchainNetwork.ETHEREUM);

        await _sut.ExecuteAsync();

        Assert.Empty(ethereum.Calls);
        Assert.DoesNotContain(_registry.Requests, r => r.Network == BlockchainNetwork.ETHEREUM);
        Assert.Equal(tron.RefundRow.Id, Assert.Single(_client.Calls).BlockchainTransactionId);
        var rows = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .Where(b => ethRows.Contains(b.Id)).ToListAsync();
        Assert.All(rows, r =>
        {
            Assert.Equal(BlockchainTransactionStatus.PENDING, r.Status);
            Assert.Equal(0, r.RetryCount);
        });
    }

    [Fact]
    public async Task A_Transient_Answer_From_A_Working_Service_Does_Not_Stop_The_Network()
    {
        // The service answered (5xx with a code) — that is about the row, not
        // the service; the rest of the network's rows are still attempted.
        var rows = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            rows.Add((await SeedRefundAsync(BlockchainTransactionType.BUYER_REFUND, amount: 1m + i)).RefundRow.Id);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.TransientFailure, null, "TRANSFER_BROADCAST_FAILED", "Bandwidth exhausted");

        await _sut.ExecuteAsync();

        Assert.Equal(rows, _client.Calls.Select(c => c.BlockchainTransactionId));
    }

    [Fact]
    public async Task TransientFailure_IncrementsRetryAndSchedulesNextAttempt()
    {
        var fixture = await SeedRefundAsync(BlockchainTransactionType.BUYER_REFUND, amount: 25m);
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.TransientFailure,
            null,
            "TRANSFER_BROADCAST_REJECTED",
            "Network timeout");
        _retryPolicy.RetryDelays = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, reloaded.Status);
        Assert.Equal(1, reloaded.RetryCount);
        Assert.NotNull(reloaded.NextAttemptAt);
        Assert.Equal(
            _clock.GetUtcNow().UtcDateTime.AddMinutes(1),
            reloaded.NextAttemptAt!.Value);
        Assert.Contains("TRANSFER_BROADCAST_REJECTED", reloaded.ErrorMessage);
        Assert.Empty(_outbox.Events.OfType<TransferDispatchFailedEvent>());
    }

    [Fact]
    public async Task TransientFailure_AfterExhaustedRetries_FlipsToFailedAndPublishesEvent()
    {
        var fixture = await SeedRefundAsync(BlockchainTransactionType.EXCESS_REFUND, amount: 10m);
        fixture.RefundRow.RetryCount = 3;
        await _db.SaveChangesAsync();

        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.TransientFailure,
            null,
            "TRANSFER_BROADCAST_FAILED",
            "Bandwidth exhausted");
        // Stub returns null delay when retryCount >= 3 — policy exhausted.
        _retryPolicy.RetryDelays = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.FAILED, reloaded.Status);
        Assert.Null(reloaded.NextAttemptAt);
        Assert.Contains("TRANSFER_BROADCAST_FAILED", reloaded.ErrorMessage);

        var failedEvent = Assert.Single(_outbox.Events.OfType<TransferDispatchFailedEvent>());
        Assert.Equal(reloaded.Id, failedEvent.BlockchainTransactionId);
        Assert.Equal(reloaded.TransactionId, failedEvent.TransactionId);
        Assert.Equal(BlockchainTransactionType.EXCESS_REFUND, failedEvent.Type);
        Assert.Equal("TRANSFER_BROADCAST_FAILED", failedEvent.LastErrorCode);
        Assert.Equal(3, failedEvent.RetryCount);
    }

    [Fact]
    public async Task InvalidRequest_TerminalFails_PublishesEvent_WithoutRetry()
    {
        var fixture = await SeedRefundAsync(BlockchainTransactionType.WRONG_TOKEN_REFUND, amount: 80m);
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.InvalidRequest,
            null,
            "INVALID_TRANSFER_REQUEST",
            "Bad payload");

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.FAILED, reloaded.Status);
        Assert.Equal(0, reloaded.RetryCount);  // No retry consumed
        Assert.Null(reloaded.NextAttemptAt);
        Assert.Contains("INVALID_TRANSFER_REQUEST", reloaded.ErrorMessage);

        Assert.Single(_outbox.Events.OfType<TransferDispatchFailedEvent>());
    }

    // The wrong-token family is the one place where the row's Token is NOT the
    // token being moved: 06 §3.8 keeps the EXPECTED stablecoin there and the
    // contract that actually landed on the deposit address in
    // ActualTokenAddress. The sidecar turns the symbol we broadcast into a
    // contract (RefundService.resolveContract), so sending row.Token would ask
    // the deposit address to move a token it does not hold. Measured on
    // 2026-09-06: it did exactly that, and nothing else in the pipeline ever
    // read ActualTokenAddress (git grep -- backend/src), so the refund could
    // not broadcast at all. The assertion below is the whole fix in one line.
    [Fact]
    public async Task WrongTokenRefund_BroadcastsTheTokenOnTheDeposit_NotTheExpectedOne()
    {
        var fixture = await SeedRefundAsync(
            BlockchainTransactionType.WRONG_TOKEN_REFUND, amount: 80m);
        // Fixture: row expects USDT, the buyer actually sent USDC.
        Assert.Equal(StablecoinType.USDT, fixture.RefundRow.Token);
        Assert.Equal(KnownStablecoinContracts.Usdc, fixture.RefundRow.ActualTokenAddress);

        await _sut.ExecuteAsync();

        var call = Assert.Single(_client.Calls);
        Assert.Equal(BlockchainTransactionType.WRONG_TOKEN_REFUND, call.Type);
        Assert.Equal(StablecoinType.USDC, call.Token);
    }

    // A contract we cannot name has no symbol to broadcast under. Failing here
    // costs the same admin alert as letting the chain reject the transfer, and
    // spends no on-chain attempts doing it. The sidecar's allowlist should keep
    // this unreachable — AmountValidationService already treats it as the
    // defensive branch — so reaching it means the allowlist and this deployment
    // disagree about which contracts exist.
    [Fact]
    public async Task WrongTokenRefund_WithUnresolvableContract_FailsTerminally_WithoutBroadcast()
    {
        var fixture = await SeedRefundAsync(
            BlockchainTransactionType.WRONG_TOKEN_REFUND, amount: 80m);
        fixture.RefundRow.ActualTokenAddress = "TUnknownContractAddressNotInAllowlist00";
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.Empty(_client.Calls);

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.FAILED, reloaded.Status);
        Assert.Equal(0, reloaded.RetryCount);
        Assert.Null(reloaded.NextAttemptAt);
        Assert.Contains("WRONG_TOKEN_CONTRACT_UNRESOLVED", reloaded.ErrorMessage);

        var failedEvent = Assert.Single(_outbox.Events.OfType<TransferDispatchFailedEvent>());
        Assert.Equal(fixture.RefundRow.Id, failedEvent.BlockchainTransactionId);
        Assert.Equal("WRONG_TOKEN_CONTRACT_UNRESOLVED", failedEvent.LastErrorCode);
    }

    [Fact]
    public async Task NotYetEligible_RowsAreSkipped()
    {
        var fixture = await SeedRefundAsync(BlockchainTransactionType.BUYER_REFUND, amount: 10m);
        fixture.RefundRow.NextAttemptAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(5);
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.Empty(_client.Calls);
        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, reloaded.Status);
        Assert.Null(reloaded.TxHash);
    }

    [Fact]
    public async Task ConfirmedRow_IsNotReDispatched()
    {
        var fixture = await SeedRefundAsync(BlockchainTransactionType.BUYER_REFUND, amount: 1m);
        fixture.RefundRow.Status = BlockchainTransactionStatus.CONFIRMED;
        fixture.RefundRow.TxHash = "0xConfirmed" + Guid.NewGuid().ToString("N");
        fixture.RefundRow.ConfirmationCount = 20;
        fixture.RefundRow.ConfirmedAt = _clock.GetUtcNow().UtcDateTime;
        fixture.RefundRow.FromAddress = fixture.PaymentAddress.Address;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.Empty(_client.Calls);
    }

    [Fact]
    public async Task Sweep_ResolvesDepositSource_AndBroadcastsToHotWallet()
    {
        // WP3 — a PENDING SWEEP is now in OutboundTypes, so the dispatcher picks
        // it up, resolves the deposit index/address via the sibling
        // BUYER_PAYMENT row (the non-SELLER_PAYOUT branch), and passes the row's
        // ToAddress (hot wallet) through. HttpBlockchainTransferClient then routes
        // it to /api/transfer/sweep (covered separately).
        var fixture = await SeedSweepAsync(amount: 100m, hotWallet: "THotWalletDispatch00000000000000000");
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.Success, "tx-hash-sweep", null, null);

        await _sut.ExecuteAsync();

        var call = Assert.Single(_client.Calls);
        Assert.Equal(BlockchainTransactionType.SWEEP, call.Type);
        Assert.Equal(fixture.PaymentAddress.HdWalletIndex, call.DepositIndex);
        Assert.Equal(fixture.PaymentAddress.Address, call.DepositAddress);
        Assert.Equal("THotWalletDispatch00000000000000000", call.ToAddress);

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.SweepRow.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, reloaded.Status);
        Assert.Equal("tx-hash-sweep", reloaded.TxHash);
        Assert.Equal(fixture.PaymentAddress.Address, reloaded.FromAddress);
    }

    private async Task<SweepFixture> SeedSweepAsync(decimal amount, string hotWallet)
    {
        var (tx, paymentAddress, _) = await SeedTransactionAsync();

        var sweep = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = paymentAddress.Id,   // CK_..._Type_Sweep: NOT NULL.
            Type = BlockchainTransactionType.SWEEP,
            TxHash = null,
            FromAddress = paymentAddress.Address,
            ToAddress = hotWallet,
            Amount = amount,
            Token = StablecoinType.USDT,
            ActualTokenAddress = null,
            GasFee = null,
            Status = BlockchainTransactionStatus.PENDING,
            ConfirmationCount = 0,
            RetryCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<BlockchainTransaction>().Add(sweep);
        await _db.SaveChangesAsync();

        return new SweepFixture(tx, paymentAddress, sweep);
    }

    private async Task<RefundFixture> SeedRefundAsync(
        BlockchainTransactionType refundType,
        decimal amount)
    {
        var (tx, paymentAddress, buyerPayment) = await SeedTransactionAsync();

        var refund = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = null,
            Type = refundType,
            TxHash = null,
            FromAddress = string.Empty,
            ToAddress = buyerPayment.FromAddress,
            Amount = amount,
            Token = StablecoinType.USDT,
            ActualTokenAddress = refundType == BlockchainTransactionType.WRONG_TOKEN_REFUND
                ? "TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8"
                : null,
            Status = BlockchainTransactionStatus.PENDING,
            ConfirmationCount = 0,
            RetryCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<BlockchainTransaction>().Add(refund);
        await _db.SaveChangesAsync();

        return new RefundFixture(tx, paymentAddress, refund);
    }

    private async Task<PayoutFixture> SeedPayoutAsync(decimal amount)
    {
        var (tx, _, _) = await SeedTransactionAsync();

        var payout = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = null,
            Type = BlockchainTransactionType.SELLER_PAYOUT,
            TxHash = null,
            FromAddress = string.Empty,
            ToAddress = tx.SellerPayoutAddress,
            Amount = amount,
            Token = StablecoinType.USDT,
            Status = BlockchainTransactionStatus.PENDING,
            ConfirmationCount = 0,
            RetryCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<BlockchainTransaction>().Add(payout);
        await _db.SaveChangesAsync();

        return new PayoutFixture(tx, payout);
    }

    private async Task<(Transaction, PaymentAddress, BlockchainTransaction)> SeedTransactionAsync()
    {
        // Unique per call (SteamId and (Network, HdWalletIndex) are unique) so a
        // test can seed a whole batch of transactions.
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
            Status = TransactionStatus.SELLER_CONFIRMED,
            SellerId = seller.Id,
            BuyerId = buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000901",
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

        var paymentAddress = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Address = "TDeposit" + Guid.NewGuid().ToString("N").Substring(0, 26),
            HdWalletIndex = 6 + seq,
            ExpectedAmount = 100m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };

        var buyerPayment = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = paymentAddress.Id,
            Type = BlockchainTransactionType.BUYER_PAYMENT,
            TxHash = "0xPayment" + Guid.NewGuid().ToString("N"),
            FromAddress = "TBuyerSource0000000000000000000000000",
            ToAddress = paymentAddress.Address,
            Amount = 100m,
            Token = StablecoinType.USDT,
            Status = BlockchainTransactionStatus.CONFIRMED,
            BlockNumber = 1_500_000L,
            ConfirmationCount = 20,
            ConfirmedAt = _clock.GetUtcNow().UtcDateTime,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };

        _db.Set<Transaction>().Add(tx);
        _db.Set<PaymentAddress>().Add(paymentAddress);
        _db.Set<BlockchainTransaction>().Add(buyerPayment);
        await _db.SaveChangesAsync();

        return (tx, paymentAddress, buyerPayment);
    }

    private sealed record RefundFixture(
        Transaction Transaction,
        PaymentAddress PaymentAddress,
        BlockchainTransaction RefundRow);

    private sealed record PayoutFixture(Transaction Transaction, BlockchainTransaction PayoutRow);

    private sealed record SweepFixture(
        Transaction Transaction,
        PaymentAddress PaymentAddress,
        BlockchainTransaction SweepRow);

    private sealed class StubBlockchainTransferClient : IBlockchainTransferClient
    {
        public TransferBroadcastResult NextResult { get; set; } =
            new(TransferBroadcastStatus.Success, "tx-stub", null, null);

        // Static: two networks' stubs in one test must not mint the same hash.
        private static int _hashSeq;

        public TransferStatusResult NextStatus { get; set; } =
            new(TransferStatusOutcome.Pending, null, 0, null, null);

        public List<TransferBroadcastRequest> Calls { get; } = new();
        public List<string> StatusCalls { get; } = new();

        public Task<TransferBroadcastResult> BroadcastAsync(
            TransferBroadcastRequest request,
            CancellationToken cancellationToken)
        {
            Calls.Add(request);
            var result = NextResult;
            if (result.Status == TransferBroadcastStatus.Success && result.TxHash == "tx-stub")
            {
                // Her çağrıya benzersiz hash: TxHash unique index'i (SQL Server).
                result = result with { TxHash = $"tx-stub-{++_hashSeq}" };
            }
            return Task.FromResult(result);
        }

        public Task<TransferStatusResult> GetStatusAsync(
            string txHash, CancellationToken cancellationToken)
        {
            StatusCalls.Add(txHash);
            return Task.FromResult(NextStatus);
        }
    }

    private sealed class StubRetryPolicy : ITransferRetryPolicy
    {
        public IReadOnlyList<TimeSpan> RetryDelays { get; set; } =
        [
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
        ];

        public Task<int> GetMaxAttemptsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(RetryDelays.Count + 1);

        public Task<TimeSpan?> GetRetryDelayAsync(int retryCount, CancellationToken cancellationToken)
        {
            if (retryCount < 0 || retryCount >= RetryDelays.Count)
                return Task.FromResult<TimeSpan?>(null);
            return Task.FromResult<TimeSpan?>(RetryDelays[retryCount]);
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
