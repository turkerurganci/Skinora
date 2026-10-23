using Skinora.Platform.Application.Wallets;
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
    private readonly DbContextOptions<AppDbContext> _options;
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
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(_options);
        _db.Database.EnsureCreated();
        // T169 — the payout/refund launch gate is seeded CLOSED. The tests
        // outside the "approval gate" section exercise the send path itself, so
        // they run with the gate open; the gate's own tests close it again.
        SetAutoSend("true");

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
            _settings,
            _observer,
            _walletAddresses,
            _limitObserver,
            _clock,
            NullLogger<OutgoingTransferDispatchJob>.Instance);
    }

    private void SetAutoSend(string? value)
    {
        var row = _db.Set<Skinora.Platform.Domain.Entities.SystemSetting>()
            .Single(s => s.Key == OutboundApprovalGate.AutoSendKey);
        row.Value = value;
        _db.SaveChanges();
    }

    /// <summary>
    /// T169 doğrulama S4-1 — an admin changing the gate while a tick runs: the
    /// write comes from another context (the settings endpoint's), so it never
    /// touches the dispatcher's tracked rows.
    /// </summary>
    private void SetAutoSendFromAnotherSession(string value)
    {
        using var other = new AppDbContext(_options);
        var row = other.Set<Skinora.Platform.Domain.Entities.SystemSetting>()
            .Single(s => s.Key == OutboundApprovalGate.AutoSendKey);
        row.Value = value;
        other.SaveChanges();
    }

    private readonly SeededNetworkSettings _settings = new();
    private readonly CapturingQueueObserver _observer = new();
    private readonly CapturingLimitObserver _limitObserver = new();
    private readonly StubWalletAddresses _walletAddresses = new();

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
        Assert.Null(reloaded.PlatformNetworkCost); // nothing reported → not measured, not 0
    }

    [Fact]
    public async Task T160c_Tron_Broadcast_Records_What_The_Send_Cost_The_Hot_Wallet()
    {
        // T159b senaryo 6 (Nile): 1 SUN + 1.1 TRX activation + a 1.611499 TRX burn top-up.
        var fixture = await SeedRefundAsync(BlockchainTransactionType.INCORRECT_AMOUNT_REFUND, amount: 2.30m);
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.Success, "tx-hash-006", null, null, PlatformSpendSun: 2_711_500L);

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, reloaded.Status);
        Assert.Equal(2_711_500m, reloaded.PlatformNetworkCost);
        Assert.Null(reloaded.PlatformNetworkCostUsdt); // valued at confirmation, not here
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

    // -------------------- T160c — collection of withheld refund fees --------------------

    [Fact]
    public async Task T160c_RefundFeeSweep_Leaves_The_Deposit_For_The_Hot_Wallet_Like_A_Sweep()
    {
        var fixture = await SeedSweepAsync(amount: 1.37m, hotWallet: "THotWalletDispatch00000000000000000");
        fixture.SweepRow.Type = BlockchainTransactionType.REFUND_FEE_SWEEP;
        fixture.SweepRow.RefundFeeRecovered = 1.37m;
        await _db.SaveChangesAsync();
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.Success, "tx-hash-fee-sweep", null, null);

        await _sut.ExecuteAsync();

        var call = Assert.Single(_client.Calls);
        Assert.Equal(BlockchainTransactionType.REFUND_FEE_SWEEP, call.Type);
        Assert.Equal(1.37m, call.Amount);
        Assert.Equal(fixture.PaymentAddress.HdWalletIndex, call.DepositIndex);
        Assert.Equal(fixture.PaymentAddress.Address, call.DepositAddress);
        Assert.Equal("THotWalletDispatch00000000000000000", call.ToAddress);
        Assert.False(call.FromHotWallet);
        Assert.Null(call.FeeCapUsd); // TRON: no cap.
        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.SweepRow.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, reloaded.Status);
    }

    [Theory]
    [InlineData(0.31, 0.31)]   // below the cap: the signer may spend at most what is collected
    [InlineData(0.75, 0.75)]   // exactly the cap
    [InlineData(2.43, 0.75)]   // above the cap: the cap
    public async Task T160c_Ethereum_RefundFeeSweep_Is_Capped_At_The_Smaller_Of_The_Cap_And_The_Amount(
        double amount, double expectedCap)
    {
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        _settings.EthereumFeeCap = 0.75m;
        var fixture = await SeedSweepAsync(amount: (decimal)amount, hotWallet: "0xhot");
        fixture.SweepRow.Type = BlockchainTransactionType.REFUND_FEE_SWEEP;
        fixture.SweepRow.RefundFeeRecovered = (decimal)amount;
        fixture.SweepRow.Network = BlockchainNetwork.ETHEREUM;
        var sale = await SeedSweepAsync(amount: 20m, hotWallet: "0xhot");
        sale.SweepRow.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        Assert.Equal((decimal)expectedCap,
            ethereum.Calls.Single(c => c.Type == BlockchainTransactionType.REFUND_FEE_SWEEP).FeeCapUsd);
        // The sale's own sweep keeps the whole cap.
        Assert.Equal(0.75m, ethereum.Calls.Single(c => c.Type == BlockchainTransactionType.SWEEP).FeeCapUsd);
    }

    // -------------------- T152 — fee cap and deferral (08 §3.7.7) --------------------

    [Fact]
    public async Task Every_Ethereum_Send_Carries_The_Fee_Cap_Setting_Tron_Does_Not()
    {
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        _settings.EthereumFeeCap = 0.75m;

        var ethPayout = await SeedPayoutAsync(amount: 20m);
        ethPayout.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
        var ethSweep = await SeedSweepAsync(amount: 20m, hotWallet: "0xhot");
        ethSweep.SweepRow.Network = BlockchainNetwork.ETHEREUM;
        var ethRefund = await SeedRefundAsync(BlockchainTransactionType.BUYER_REFUND, amount: 5m);
        ethRefund.RefundRow.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();
        var tronPayout = await SeedPayoutAsync(amount: 30m);

        await _sut.ExecuteAsync();

        Assert.Equal(0.75m, ethereum.Calls.Single(c => c.Type == BlockchainTransactionType.SELLER_PAYOUT).FeeCapUsd);
        Assert.Equal(0.75m, ethereum.Calls.Single(c => c.Type == BlockchainTransactionType.SWEEP).FeeCapUsd);
        // Yönetici kararı 2026-10-18: refunds are capped too (they wait above it).
        Assert.Equal(0.75m, ethereum.Calls.Single(c => c.Type == BlockchainTransactionType.BUYER_REFUND).FeeCapUsd);
        Assert.Null(Assert.Single(_client.Calls, c => c.BlockchainTransactionId == tronPayout.PayoutRow.Id).FeeCapUsd);
    }

    [Fact]
    public async Task Deferred_Row_Stays_Pending_Without_Spending_A_Retry_And_Does_Not_Stop_The_Network()
    {
        var ethereum = new StubBlockchainTransferClient
        {
            NextResult = new TransferBroadcastResult(
                TransferBroadcastStatus.Deferred, null, "FEE_ABOVE_CAP", "fee 4 gwei above cap 3.3 gwei"),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var first = await SeedPayoutAsync(amount: 20m);
        first.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
        first.PayoutRow.RetryCount = 1;
        var second = await SeedPayoutAsync(amount: 21m);
        second.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        // Both asked in the same run: a wait is not an outage.
        Assert.Equal(2, ethereum.Calls.Count);
        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == first.PayoutRow.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, reloaded.Status);
        Assert.Equal(1, reloaded.RetryCount);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime + OutgoingTransferDispatchJob.DeferredRetryDelay, reloaded.NextAttemptAt);
        Assert.StartsWith("FEE_ABOVE_CAP", reloaded.ErrorMessage);
        Assert.Null(reloaded.TxHash);
        Assert.Empty(_outbox.Events.OfType<TransferDispatchFailedEvent>());

        // Many waits never exhaust the retry budget (a 3-hour wait is ~180 runs).
        for (var i = 0; i < 10; i++)
        {
            _clock.Advance(OutgoingTransferDispatchJob.DeferredRetryDelay);
            await _sut.ExecuteAsync();
        }
        reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == first.PayoutRow.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, reloaded.Status);
        Assert.Equal(1, reloaded.RetryCount);
        Assert.Empty(_outbox.Events.OfType<TransferDispatchFailedEvent>());
    }

    // -------------------- T152 düzeltme turu — Ethereum never FAILs on a transient error --------------------

    [Theory]
    [InlineData(HttpBlockchainTransferClient.TransportFailureCode, "timeout / connection lost")]
    [InlineData("INTERNAL_ERROR", "sidecar 500")]
    [InlineData("RPC_ALL_PROVIDERS_FAILED", "sidecar 502")]
    public async Task Ethereum_Transient_Failures_Past_The_Retry_Budget_Never_Fail_The_Row_And_Reuse_Its_Id(
        string code, string message)
    {
        // Proje sahibi kararı 2026-10-18: the Ethereum signer answers a repeated
        // id with the same transaction, so after a timeout the row keeps trying
        // with the SAME id instead of becoming FAILED (which would invite a
        // manual re-send under a new id).
        var ethereum = new StubBlockchainTransferClient
        {
            NextResult = new TransferBroadcastResult(TransferBroadcastStatus.TransientFailure, null, code, message),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var fixture = await SeedPayoutAsync(amount: 20m);
        fixture.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        const int runs = 8; // retry budget is 3 (1, 5, 15 min)
        for (var i = 0; i < runs; i++)
        {
            await _sut.ExecuteAsync();
            _clock.Advance(TimeSpan.FromMinutes(16));
        }

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.PayoutRow.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, reloaded.Status);
        Assert.Equal(runs, reloaded.RetryCount);
        Assert.Empty(_outbox.Events.OfType<TransferDispatchFailedEvent>());
        Assert.Equal(runs, ethereum.Calls.Count);
        Assert.All(ethereum.Calls, c => Assert.Equal(fixture.PayoutRow.Id, c.BlockchainTransactionId));
        // Past the budget it keeps the policy's last interval.
        Assert.Equal(_clock.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(16) + TimeSpan.FromMinutes(15), reloaded.NextAttemptAt);
    }

    [Fact]
    public async Task Ethereum_Permanent_Refusal_Still_Fails_The_Row_With_An_Admin_Event()
    {
        var ethereum = new StubBlockchainTransferClient
        {
            NextResult = new TransferBroadcastResult(
                TransferBroadcastStatus.InvalidRequest, null, "TOKEN_CONTRACT_UNSAFE", "USDT paused"),
        };
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        var fixture = await SeedPayoutAsync(amount: 20m);
        fixture.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.PayoutRow.Id);
        Assert.Equal(BlockchainTransactionStatus.FAILED, reloaded.Status);
        Assert.Equal("TOKEN_CONTRACT_UNSAFE", Assert.Single(_outbox.Events.OfType<TransferDispatchFailedEvent>()).LastErrorCode);
    }

    [Fact]
    public async Task Tron_Still_Fails_After_The_Retry_Budget()
    {
        var fixture = await SeedPayoutAsync(amount: 20m);
        fixture.PayoutRow.RetryCount = 3;
        await _db.SaveChangesAsync();
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.TransientFailure, null, HttpBlockchainTransferClient.TransportFailureCode, "timeout");

        await _sut.ExecuteAsync();

        var reloaded = await _db.Set<BlockchainTransaction>().AsNoTracking()
            .FirstAsync(b => b.Id == fixture.PayoutRow.Id);
        Assert.Equal(BlockchainTransactionStatus.FAILED, reloaded.Status);
    }

    [Fact]
    public async Task Every_Run_Reports_Each_Networks_Pending_Count_Oldest_Age_And_Threshold()
    {
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: new StubBlockchainTransferClient
        {
            NextResult = new TransferBroadcastResult(TransferBroadcastStatus.TransientFailure, null, "INTERNAL_ERROR", "500"),
        });
        var first = await SeedPayoutAsync(amount: 20m);
        first.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
        _clock.Advance(TimeSpan.FromMinutes(30));
        var second = await SeedPayoutAsync(amount: 21m);
        second.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();
        _clock.Advance(TimeSpan.FromHours(3));
        _health.Degraded.Add(BlockchainNetwork.ETHEREUM); // reported even while skipped

        await _sut.ExecuteAsync();

        var eth = _observer.Last[BlockchainNetwork.ETHEREUM];
        Assert.Equal(2, eth.Rows);
        Assert.Equal(TimeSpan.FromMinutes(210).TotalSeconds, eth.Oldest);
        Assert.Equal(180 * 60d, eth.Alarm);
        var tron = _observer.Last[BlockchainNetwork.TRON];
        Assert.Equal(0, tron.Rows);
        Assert.Null(tron.Alarm);
    }

    // -------------------- T169 — payout/refund approval gate (⚠ PARA) --------------------

    [Fact]
    public void T169_The_Gate_Is_Seeded_Closed()
    {
        var seeded = Skinora.Platform.Infrastructure.Persistence.SystemSettingSeed.All
            .Single(s => s.Key == OutboundApprovalGate.AutoSendKey);
        Assert.Equal("false", seeded.Value);
        Assert.True(seeded.IsConfigured); // not env-bootstrappable (the T125 rule)
    }

    [Fact]
    public async Task T169_Gate_Closed_Unapproved_Payout_And_Refund_Wait_Untouched_Sweep_Still_Goes()
    {
        SetAutoSend("false");
        var payout = await SeedPayoutAsync(amount: 100m);
        var refund = await SeedRefundAsync(BlockchainTransactionType.BUYER_REFUND, amount: 40m);
        await SeedSweepAsync(amount: 100m, hotWallet: "THotWalletDispatch00000000000000000");
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.Success, "tx-hash-gate", null, null);

        await _sut.ExecuteAsync();

        // Only the sweep is sent.
        var call = Assert.Single(_client.Calls);
        Assert.Equal(BlockchainTransactionType.SWEEP, call.Type);
        foreach (var id in new[] { payout.PayoutRow.Id, refund.RefundRow.Id })
        {
            var row = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == id);
            Assert.Equal(BlockchainTransactionStatus.PENDING, row.Status);
            Assert.Null(row.TxHash);
            Assert.Equal(0, row.RetryCount);
            Assert.Null(row.NextAttemptAt);
            Assert.Null(row.ErrorMessage);
        }
        Assert.Empty(_outbox.Events.OfType<TransferDispatchFailedEvent>());
    }

    [Theory]
    [InlineData(BlockchainTransactionType.EXCESS_REFUND)]
    [InlineData(BlockchainTransactionType.INCORRECT_AMOUNT_REFUND)]
    [InlineData(BlockchainTransactionType.LATE_PAYMENT_REFUND)]
    [InlineData(BlockchainTransactionType.WRONG_TOKEN_REFUND)]
    public async Task T169_Gate_Closed_Every_Refund_Type_Waits(BlockchainTransactionType type)
    {
        SetAutoSend("false");
        await SeedRefundAsync(type, amount: 30m);

        await _sut.ExecuteAsync();

        Assert.Empty(_client.Calls);
    }

    [Fact]
    public async Task T169_Gate_Closed_An_Approved_Row_Is_Sent_Exactly_As_Without_The_Gate()
    {
        SetAutoSend("false");
        var payout = await SeedPayoutAsync(amount: 100m);
        var waiting = await SeedRefundAsync(BlockchainTransactionType.BUYER_REFUND, amount: 40m);
        payout.PayoutRow.ApprovedAt = _clock.GetUtcNow().UtcDateTime;
        payout.PayoutRow.ApprovedByAdminId = Guid.NewGuid();
        await _db.SaveChangesAsync();
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.Success, "tx-hash-approved", null, null);

        await _sut.ExecuteAsync();

        var call = Assert.Single(_client.Calls);
        Assert.Equal(payout.PayoutRow.Id, call.BlockchainTransactionId);
        Assert.Equal(payout.PayoutRow.Amount, call.Amount);
        Assert.Equal(payout.PayoutRow.ToAddress, call.ToAddress);
        var sent = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == payout.PayoutRow.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, sent.Status);
        var held = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == waiting.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, held.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("false")]
    public async Task T169_An_Unreadable_Or_False_Gate_Reads_Closed(string? value)
    {
        SetAutoSend(value);
        await SeedPayoutAsync(amount: 100m);

        await _sut.ExecuteAsync();

        Assert.Empty(_client.Calls);
    }

    [Fact]
    public async Task T169_An_Unconfigured_Gate_Row_Reads_Closed()
    {
        var row = _db.Set<Skinora.Platform.Domain.Entities.SystemSetting>()
            .Single(s => s.Key == OutboundApprovalGate.AutoSendKey);
        row.Value = "true";
        row.IsConfigured = false;
        await _db.SaveChangesAsync();
        await SeedPayoutAsync(amount: 100m);

        await _sut.ExecuteAsync();

        Assert.Empty(_client.Calls);
    }

    [Fact]
    public async Task T169_Gate_Open_Sends_Without_Approval()
    {
        SetAutoSend("True"); // bool.TryParse ignores case
        await SeedPayoutAsync(amount: 100m);
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.Success, "tx-hash-open", null, null);

        await _sut.ExecuteAsync();

        Assert.Single(_client.Calls);
    }

    [Fact]
    public async Task T169_Held_Rows_Are_Not_In_The_Stall_Queue_And_An_Approved_Row_Counts_From_Its_Approval()
    {
        SetAutoSend("false");
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: new StubBlockchainTransferClient
        {
            NextResult = new TransferBroadcastResult(TransferBroadcastStatus.TransientFailure, null, "INTERNAL_ERROR", "500"),
        });
        var held = await SeedPayoutAsync(amount: 20m);
        held.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
        var approved = await SeedPayoutAsync(amount: 21m);
        approved.PayoutRow.Network = BlockchainNetwork.ETHEREUM;
        await _db.SaveChangesAsync();
        _clock.Advance(TimeSpan.FromHours(5));               // both rows were created 5 h ago
        approved.PayoutRow.ApprovedAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(-10);
        await _db.SaveChangesAsync();
        _health.Degraded.Add(BlockchainNetwork.ETHEREUM);     // reported, nothing sent

        await _sut.ExecuteAsync();

        var eth = _observer.Last[BlockchainNetwork.ETHEREUM];
        Assert.Equal(1, eth.Rows);                            // the held row waits for a person, not the fee
        Assert.Equal(TimeSpan.FromMinutes(10).TotalSeconds, eth.Oldest);
    }

    // T165 — the approval queue is reported every run for every network, so the
    // outbound-awaiting-approval money alarm fires while rows wait and resolves
    // once they are approved (or the gate is opened).
    [Fact]
    public async Task T165_Awaiting_Approval_Is_Reported_Per_Network_And_Clears_On_Approval()
    {
        SetAutoSend("false");
        var payout = await SeedPayoutAsync(amount: 100m);
        await SeedRefundAsync(BlockchainTransactionType.BUYER_REFUND, amount: 40m);
        await SeedSweepAsync(amount: 100m, hotWallet: "THotWalletDispatch00000000000000000");
        _clock.Advance(TimeSpan.FromMinutes(7));
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.Success, "tx-hash-sweep", null, null);

        await _sut.ExecuteAsync();

        var tron = _observer.Awaiting[BlockchainNetwork.TRON];
        Assert.Equal(2, tron.Rows);                                   // the sweep never waits
        Assert.Equal(TimeSpan.FromMinutes(7).TotalSeconds, tron.Oldest);
        Assert.Equal((0, 0d), _observer.Awaiting[BlockchainNetwork.ETHEREUM]);

        // Approving one leaves one waiting; approving both clears the series.
        var refundRow = await _db.Set<BlockchainTransaction>()
            .FirstAsync(b => b.Type == BlockchainTransactionType.BUYER_REFUND);
        payout.PayoutRow.ApprovedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync();
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.Success, "tx-hash-payout", null, null);
        await _sut.ExecuteAsync();
        Assert.Equal(1, _observer.Awaiting[BlockchainNetwork.TRON].Rows);

        refundRow.ApprovedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync();
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.Success, "tx-hash-refund", null, null);
        await _sut.ExecuteAsync();
        Assert.Equal((0, 0d), _observer.Awaiting[BlockchainNetwork.TRON]);
    }

    [Fact]
    public async Task T165_Gate_Open_Reports_Nothing_Awaiting_Approval()
    {
        SetAutoSend("true");
        await SeedRefundAsync(BlockchainTransactionType.BUYER_REFUND, amount: 40m);
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.Success, "tx-hash-open", null, null);

        await _sut.ExecuteAsync();

        Assert.Equal((0, 0d), _observer.Awaiting[BlockchainNetwork.TRON]);
        Assert.Equal((0, 0d), _observer.Awaiting[BlockchainNetwork.ETHEREUM]);
    }

    [Fact]
    public async Task T169_Gate_Closed_Mid_Tick_Stops_Every_Unapproved_Row_Left_In_The_Batch()
    {
        // Doğrulama S4-1 (⚠ PARA, probe P5 reproduced): the gate is open when
        // the tick starts and picks four rows; the admin closes it while the
        // first one is being sent. Before the fix the tick-start value let the
        // three unapproved payouts out (3/3); now none of them leaves (0/3).
        SetAutoSend("true");
        var approved = await SeedPayoutAsync(amount: 10m);
        approved.PayoutRow.ApprovedAt = _clock.GetUtcNow().UtcDateTime;
        approved.PayoutRow.ApprovedByAdminId = Guid.NewGuid();
        await _db.SaveChangesAsync();
        var unapproved = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1)); // oldest first: the approved row goes first
            unapproved.Add((await SeedPayoutAsync(amount: 20m + i)).PayoutRow.Id);
        }
        _clock.Advance(TimeSpan.FromSeconds(1));
        var sweep = await SeedSweepAsync(amount: 100m, hotWallet: "THotWalletDispatch00000000000000000");
        _client.OnBroadcast = _ =>
        {
            if (_client.Calls.Count == 1) SetAutoSendFromAnotherSession("false");
        };

        await _sut.ExecuteAsync();

        // The approved payout (already sending when the gate closed) and the
        // sweep (never gated) go; no unapproved payout does.
        Assert.Equal(
            new[] { approved.PayoutRow.Id, sweep.SweepRow.Id },
            _client.Calls.Select(c => c.BlockchainTransactionId).ToArray());
        foreach (var id in unapproved)
        {
            var row = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == id);
            Assert.Equal(BlockchainTransactionStatus.PENDING, row.Status);
            Assert.Null(row.TxHash);
            Assert.Equal(0, row.RetryCount);
            Assert.Null(row.NextAttemptAt);
            Assert.Null(row.ErrorMessage);
        }
        Assert.Empty(_outbox.Events.OfType<TransferDispatchFailedEvent>());
    }

    [Fact]
    public async Task T169_Gate_Opened_Mid_Tick_Opens_On_The_Next_Tick()
    {
        // The other direction is harmless and stays one tick late: rows the
        // closed gate left out of this tick's batch go on the next one.
        SetAutoSend("false");
        var approved = await SeedPayoutAsync(amount: 10m);
        approved.PayoutRow.ApprovedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync();
        _clock.Advance(TimeSpan.FromSeconds(1));
        var unapproved = await SeedPayoutAsync(amount: 20m);
        _client.OnBroadcast = _ =>
        {
            if (_client.Calls.Count == 1) SetAutoSendFromAnotherSession("true");
        };

        await _sut.ExecuteAsync();
        Assert.Equal(new[] { approved.PayoutRow.Id }, _client.Calls.Select(c => c.BlockchainTransactionId).ToArray());

        await _sut.ExecuteAsync();
        Assert.Equal(
            new[] { approved.PayoutRow.Id, unapproved.PayoutRow.Id },
            _client.Calls.Select(c => c.BlockchainTransactionId).ToArray());
    }

    [Fact]
    public async Task T169_Held_Rows_Do_Not_Take_Batch_Slots_From_An_Approved_Row()
    {
        // Doğrulama S4-2 — 07 §9.35 "parti yerini de almaz": more unapproved
        // rows than a batch holds, all older than one approved payout. The
        // candidate query must leave them out; were it to pick them, the second
        // layer would hold every one of them and the approved row would never
        // be reached (removing the gate condition from the query breaks this).
        SetAutoSend("false");
        for (var i = 0; i < OutgoingTransferDispatchJob.BatchSize + 5; i++)
        {
            await SeedPayoutAsync(amount: 20m);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }
        var approved = await SeedPayoutAsync(amount: 10m);
        approved.PayoutRow.ApprovedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var call = Assert.Single(_client.Calls);
        Assert.Equal(approved.PayoutRow.Id, call.BlockchainTransactionId);
    }

    /// <summary>T158 (f) — the Ethereum hot wallet a post-sweep refund leaves from.</summary>
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

    // ---- T169a — deposit refund daily limit (proje sahibi kararı 2026-10-21, ⚠ PARA) ----
    // Fixture limits are never the seeded 100: a test that passes on the seed
    // value alone would not show the setting is read.

    /// <summary>A TRON deposit refund of <paramref name="amount"/>, queued one second after the previous seed.</summary>
    private async Task<RefundFixture> SeedLimitRefundAsync(decimal amount,
        BlockchainTransactionType type = BlockchainTransactionType.EXCESS_REFUND)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));
        return await SeedRefundAsync(type, amount);
    }

    private async Task<BlockchainTransaction> ReloadAsync(Guid id) =>
        await _db.Set<BlockchainTransaction>().AsNoTracking().IgnoreQueryFilters().FirstAsync(b => b.Id == id);

    [Fact]
    public async Task T169a_Deposit_Refunds_Stop_At_The_Limit_The_One_That_Would_Cross_It_Waits_And_Alarms_Once()
    {
        // 30 + 35 = 65 ≤ 70; + 20 = 85 > 70 → the third waits.
        _settings.DepositRefundDailyLimit = 70m;
        var first = await SeedLimitRefundAsync(30m);
        var second = await SeedLimitRefundAsync(35m, BlockchainTransactionType.INCORRECT_AMOUNT_REFUND);
        var third = await SeedLimitRefundAsync(20m, BlockchainTransactionType.LATE_PAYMENT_REFUND);

        await _sut.ExecuteAsync();

        Assert.Equal([first.RefundRow.Id, second.RefundRow.Id], _client.Calls.Select(c => c.BlockchainTransactionId));
        var now = _clock.GetUtcNow().UtcDateTime;
        Assert.Equal(now, (await ReloadAsync(first.RefundRow.Id)).DepositRefundCountedAt);
        var held = await ReloadAsync(third.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, held.Status);
        Assert.Equal(0, held.RetryCount);
        Assert.Null(held.DepositRefundCountedAt);
        Assert.Equal(now, held.DepositRefundLimitHeldAt);
        Assert.Equal(now + DepositRefundDailyLimit.HeldRetryDelay, held.NextAttemptAt);
        Assert.StartsWith(DepositRefundDailyLimit.HeldErrorCode, held.ErrorMessage);
        var alert = Assert.IsType<DepositRefundLimitHeldEvent>(Assert.Single(_outbox.Events));
        Assert.Equal(third.RefundRow.Id, alert.BlockchainTransactionId);
        Assert.Equal(BlockchainNetwork.TRON, alert.Network);
        Assert.Equal(20m, alert.Amount);
        Assert.Equal(65m, alert.UsedLast24hUsdt);
        Assert.Equal(70m, alert.LimitUsdt);
        Assert.Equal((1, 65m, 70m), _limitObserver.Last[BlockchainNetwork.TRON]);

        // The next ticks within the quarter hour do not even pick it up; the
        // one after weighs it again, still above — no second alarm.
        _clock.Advance(DepositRefundDailyLimit.HeldRetryDelay);
        await _sut.ExecuteAsync();
        Assert.Equal(2, _client.Calls.Count);
        Assert.Single(_outbox.Events);
        Assert.Equal(now, (await ReloadAsync(third.RefundRow.Id)).DepositRefundLimitHeldAt);
    }

    [Fact]
    public async Task T169a_Exactly_The_Limit_Passes_A_Cent_Above_It_Waits()
    {
        _settings.DepositRefundDailyLimit = 48.50m;
        await SeedLimitRefundAsync(20.25m);
        await SeedLimitRefundAsync(28.25m);
        var cent = await SeedLimitRefundAsync(0.01m);

        await _sut.ExecuteAsync();

        Assert.Equal(2, _client.Calls.Count);
        Assert.NotNull((await ReloadAsync(cent.RefundRow.Id)).DepositRefundLimitHeldAt);
    }

    [Fact]
    public async Task T169a_A_Held_Refund_Goes_Out_When_The_Rolling_Window_Has_Room_Not_At_Midnight()
    {
        _settings.DepositRefundDailyLimit = 70m;
        var sent = await SeedLimitRefundAsync(60m);
        await _sut.ExecuteAsync();
        var held = await SeedLimitRefundAsync(20m);
        await _sut.ExecuteAsync();
        Assert.NotNull((await ReloadAsync(held.RefundRow.Id)).DepositRefundLimitHeldAt);
        var sentAt = (await ReloadAsync(sent.RefundRow.Id)).DepositRefundCountedAt!.Value;

        // One minute short of 24 h after the 60 was sent: still in the window.
        _clock.SetUtcNow(new DateTimeOffset(sentAt + DepositRefundDailyLimit.Window - TimeSpan.FromMinutes(1), TimeSpan.Zero));
        await _sut.ExecuteAsync();
        Assert.Single(_client.Calls);

        // That weighing set the next one a quarter of an hour later; a minute
        // after the window frees, the row is not due yet.
        _clock.SetUtcNow(new DateTimeOffset(sentAt + DepositRefundDailyLimit.Window + TimeSpan.FromMinutes(1), TimeSpan.Zero));
        await _sut.ExecuteAsync();
        Assert.Single(_client.Calls);

        // Due again, the 60 is out of the window: 0 + 20 ≤ 70.
        _clock.SetUtcNow(new DateTimeOffset(
            sentAt + DepositRefundDailyLimit.Window - TimeSpan.FromMinutes(1) + DepositRefundDailyLimit.HeldRetryDelay,
            TimeSpan.Zero));
        await _sut.ExecuteAsync();

        Assert.Equal(held.RefundRow.Id, _client.Calls[^1].BlockchainTransactionId);
        var row = await ReloadAsync(held.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, row.Status);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, row.DepositRefundCountedAt);
        Assert.Single(_outbox.Events); // one alarm for the whole hold
    }

    [Fact]
    public async Task T169a_A_Refund_Larger_Than_The_Limit_Never_Passes_On_Its_Own()
    {
        _settings.DepositRefundDailyLimit = 70m;
        var big = await SeedLimitRefundAsync(70.01m, BlockchainTransactionType.BUYER_REFUND);

        await _sut.ExecuteAsync();
        _clock.Advance(TimeSpan.FromDays(2));
        await _sut.ExecuteAsync();

        Assert.Empty(_client.Calls);
        Assert.Equal(BlockchainTransactionStatus.PENDING, (await ReloadAsync(big.RefundRow.Id)).Status);
    }

    [Fact]
    public async Task T169a_An_Admin_Release_Lets_One_Held_Refund_Go_And_It_Still_Counts()
    {
        _settings.DepositRefundDailyLimit = 50m;
        await SeedLimitRefundAsync(40m);
        var held = await SeedLimitRefundAsync(30m);
        await _sut.ExecuteAsync();
        Assert.Single(_client.Calls);

        var tracked = await _db.Set<BlockchainTransaction>().FirstAsync(b => b.Id == held.RefundRow.Id);
        tracked.DepositRefundLimitReleasedAt = _clock.GetUtcNow().UtcDateTime;
        tracked.NextAttemptAt = null;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _sut.ExecuteAsync();

        Assert.Equal(held.RefundRow.Id, _client.Calls[^1].BlockchainTransactionId);
        // 40 + 30 = 70 sent: the release counts, so even 1 more waits.
        var next = await SeedLimitRefundAsync(1m);
        await _sut.ExecuteAsync();
        Assert.Equal(2, _client.Calls.Count);
        Assert.NotNull((await ReloadAsync(next.RefundRow.Id)).DepositRefundLimitHeldAt);
        Assert.Equal((1, 70m, 50m), _limitObserver.Last[BlockchainNetwork.TRON]);
    }

    [Fact]
    public async Task T169a_The_Approval_Gate_Comes_First_And_An_Approval_Does_Not_Lift_The_Limit()
    {
        _settings.DepositRefundDailyLimit = 25m;
        SetAutoSend("false");
        var refund = await SeedLimitRefundAsync(30m);

        await _sut.ExecuteAsync();

        // Unapproved: never weighed — no hold, no alarm.
        Assert.Empty(_client.Calls);
        Assert.Null((await ReloadAsync(refund.RefundRow.Id)).DepositRefundLimitHeldAt);
        Assert.Empty(_outbox.Events);

        var tracked = await _db.Set<BlockchainTransaction>().FirstAsync(b => b.Id == refund.RefundRow.Id);
        tracked.ApprovedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _sut.ExecuteAsync();

        // Approved but above the limit: held, alarmed — still nothing sent.
        Assert.Empty(_client.Calls);
        Assert.NotNull((await ReloadAsync(refund.RefundRow.Id)).DepositRefundLimitHeldAt);
        Assert.IsType<DepositRefundLimitHeldEvent>(Assert.Single(_outbox.Events));
    }

    [Fact]
    public async Task T169a_A_Retry_Of_An_Admitted_Refund_Does_Not_Count_Itself_Twice()
    {
        // 40 admitted, the broadcast fails transiently; its retry finds 40 in
        // the window — its own — and must not read 40 + 40 > 50.
        _settings.DepositRefundDailyLimit = 50m;
        var refund = await SeedLimitRefundAsync(40m);
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.TransientFailure, null, "SIDECAR_TIMEOUT", "timeout");

        await _sut.ExecuteAsync();
        var admitted = await ReloadAsync(refund.RefundRow.Id);
        Assert.Equal(1, admitted.RetryCount);
        Assert.NotNull(admitted.DepositRefundCountedAt);

        _client.NextResult = new TransferBroadcastResult(TransferBroadcastStatus.Success, "tx-stub", null, null);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await _sut.ExecuteAsync();

        Assert.Equal(2, _client.Calls.Count);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, (await ReloadAsync(refund.RefundRow.Id)).Status);
        Assert.Null((await ReloadAsync(refund.RefundRow.Id)).DepositRefundLimitHeldAt);
    }

    [Fact]
    public async Task T169a_A_Failed_Refund_Frees_Its_Room()
    {
        _settings.DepositRefundDailyLimit = 50m;
        var failed = await SeedLimitRefundAsync(40m);
        _client.NextResult = new TransferBroadcastResult(
            TransferBroadcastStatus.InvalidRequest, null, "TRANSFER_REJECTED", "rejected");
        await _sut.ExecuteAsync();
        Assert.Equal(BlockchainTransactionStatus.FAILED, (await ReloadAsync(failed.RefundRow.Id)).Status);

        _client.NextResult = new TransferBroadcastResult(TransferBroadcastStatus.Success, "tx-stub", null, null);
        var next = await SeedLimitRefundAsync(45m);
        await _sut.ExecuteAsync();

        Assert.Equal(next.RefundRow.Id, _client.Calls[^1].BlockchainTransactionId);
    }

    [Fact]
    public async Task T169a_Payouts_Sweeps_And_Hot_Wallet_Refunds_Neither_Count_Nor_Wait()
    {
        _settings.DepositRefundDailyLimit = 10m;
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        _walletAddresses.EthereumHotWalletAddress = EthereumHot;
        var payout = await SeedPayoutAsync(500m);
        var hotWalletRefund = await SeedEthereumRefundAfterSweepAsync(
            BlockchainTransactionType.BUYER_REFUND, BlockchainTransactionStatus.CONFIRMED);

        await _sut.ExecuteAsync();

        Assert.Contains(_client.Calls, c => c.BlockchainTransactionId == payout.PayoutRow.Id);
        var call = Assert.Single(ethereum.Calls);
        Assert.Equal(hotWalletRefund.RefundRow.Id, call.BlockchainTransactionId);
        Assert.True(call.FromHotWallet);
        Assert.Null((await ReloadAsync(hotWalletRefund.RefundRow.Id)).DepositRefundCountedAt);
        Assert.Null((await ReloadAsync(payout.PayoutRow.Id)).DepositRefundCountedAt);
        Assert.Equal((0, 0m, 10m), _limitObserver.Last[BlockchainNetwork.ETHEREUM]);
        Assert.Equal((0, 0m, 10m), _limitObserver.Last[BlockchainNetwork.TRON]);
    }

    [Fact]
    public async Task T169a_The_Limit_Is_Per_Network()
    {
        // TRON is full; an Ethereum deposit refund (before the sweep) still goes.
        _settings.DepositRefundDailyLimit = 99.5m;
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        await SeedLimitRefundAsync(99.5m);
        var tronHeld = await SeedLimitRefundAsync(1m);
        var ethereumRefund = await SeedEthereumRefundAfterSweepAsync(BlockchainTransactionType.EXCESS_REFUND, sweepStatus: null);

        await _sut.ExecuteAsync();

        var ethereumCall = Assert.Single(ethereum.Calls);
        Assert.Equal(ethereumRefund.RefundRow.Id, ethereumCall.BlockchainTransactionId);
        Assert.False(ethereumCall.FromHotWallet);
        Assert.NotNull((await ReloadAsync(tronHeld.RefundRow.Id)).DepositRefundLimitHeldAt);
        Assert.Equal((1, 99.5m, 99.5m), _limitObserver.Last[BlockchainNetwork.TRON]);
        Assert.Equal((0, 99m, 99.5m), _limitObserver.Last[BlockchainNetwork.ETHEREUM]);
    }

    [Fact]
    public async Task T169a_A_Held_Refund_Is_Not_In_The_Stall_Queue()
    {
        _settings.DepositRefundDailyLimit = 30m;
        await SeedLimitRefundAsync(25m);
        await SeedLimitRefundAsync(25m);

        await _sut.ExecuteAsync();
        _clock.Advance(TimeSpan.FromHours(5));
        await _sut.ExecuteAsync();

        // The held refund has its own alarm; the stall queue does not see it.
        Assert.Equal(0, _observer.Last[BlockchainNetwork.TRON].Rows);
        Assert.Equal(1, _limitObserver.Last[BlockchainNetwork.TRON].Held);
    }

    private sealed class CapturingQueueObserver : IOutboundQueueObserver
    {
        public Dictionary<BlockchainNetwork, (int Rows, double Oldest, double? Alarm)> Last { get; } = new();

        public void Report(BlockchainNetwork network, int pendingRows, double oldestPendingSeconds, double? alarmSeconds) =>
            Last[network] = (pendingRows, oldestPendingSeconds, alarmSeconds);

        public Dictionary<BlockchainNetwork, (int Rows, double Oldest)> Awaiting { get; } = new();

        public void ReportAwaitingApproval(BlockchainNetwork network, int awaitingRows, double oldestAwaitingSeconds) =>
            Awaiting[network] = (awaitingRows, oldestAwaitingSeconds);
    }

    private sealed class CapturingLimitObserver : IDepositRefundLimitObserver
    {
        public Dictionary<BlockchainNetwork, (int Held, decimal Used, decimal Limit)> Last { get; } = new();

        public void Report(BlockchainNetwork network, int heldRows, decimal usedUsdt, decimal limitUsdt) =>
            Last[network] = (heldRows, usedUsdt, limitUsdt);
    }

    private sealed class SeededNetworkSettings : IBlockchainNetworkSettingsProvider
    {
        public decimal EthereumFeeCap { get; set; } = 0.50m;

        /// <summary>
        /// T169a — the deposit refund daily limit. The send-path tests predate
        /// it and use whatever amounts they need, so it is out of their way by
        /// default; the limit's own tests set it (fixture values are not the
        /// seeded 100).
        /// </summary>
        public decimal DepositRefundDailyLimit { get; set; } = 1_000_000m;

        public Task<BlockchainNetworkSettings> GetAsync(BlockchainNetwork network, CancellationToken cancellationToken)
        {
            var defaults = BlockchainNetworkSettingsProvider.DefaultsFor(network);
            defaults = defaults with { DepositRefundDailyLimitUsdt = DepositRefundDailyLimit };
            return Task.FromResult(network == BlockchainNetwork.ETHEREUM
                ? defaults with { FeeCapUsdPerTransfer = EthereumFeeCap }
                : defaults);
        }
    }

    // ---- T158 (f) — refund after the sweep leaves the hot wallet (08 §3.7.6 "Kaynaklar") ----

    private const string EthereumHot = "0x7070707070707070707070707070707070707070";

    /// <summary>An Ethereum refund row of <paramref name="type"/> whose transaction has a SWEEP row in <paramref name="sweepStatus"/> (null = no sweep).</summary>
    private async Task<RefundFixture> SeedEthereumRefundAfterSweepAsync(
        BlockchainTransactionType type, BlockchainTransactionStatus? sweepStatus)
    {
        var fixture = await SeedRefundAsync(type, amount: 99m);
        fixture.RefundRow.Network = BlockchainNetwork.ETHEREUM;
        fixture.PaymentAddress.Network = BlockchainNetwork.ETHEREUM;
        if (sweepStatus is { } status)
        {
            _db.Set<BlockchainTransaction>().Add(new BlockchainTransaction
            {
                Id = Guid.NewGuid(),
                TransactionId = fixture.Transaction.Id,
                PaymentAddressId = fixture.PaymentAddress.Id,
                Network = BlockchainNetwork.ETHEREUM,
                Type = BlockchainTransactionType.SWEEP,
                TxHash = status == BlockchainTransactionStatus.PENDING ? null : "0xsweep" + Guid.NewGuid().ToString("N"),
                FromAddress = fixture.PaymentAddress.Address,
                ToAddress = EthereumHot,
                Amount = 100m,
                Token = StablecoinType.USDT,
                Status = status,
                BlockNumber = status == BlockchainTransactionStatus.CONFIRMED ? 26_215_000L : null,
                ConfirmationCount = 0,
                ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED ? _clock.GetUtcNow().UtcDateTime : null,
                CreatedAt = _clock.GetUtcNow().UtcDateTime,
            });
        }
        await _db.SaveChangesAsync();
        return fixture;
    }

    [Fact]
    public async Task T158f_Ethereum_BuyerRefund_After_A_Confirmed_Sweep_Leaves_The_Hot_Wallet()
    {
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        _walletAddresses.EthereumHotWalletAddress = EthereumHot.ToUpperInvariant().Replace("0X", "0x");
        var fixture = await SeedEthereumRefundAfterSweepAsync(
            BlockchainTransactionType.BUYER_REFUND, BlockchainTransactionStatus.CONFIRMED);

        await _sut.ExecuteAsync();

        var call = Assert.Single(ethereum.Calls);
        Assert.True(call.FromHotWallet);
        Assert.Null(call.DepositIndex);
        Assert.Null(call.DepositAddress);
        var row = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == fixture.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.DETECTED, row.Status);
        // Canonical hot wallet: the reconciliation counts the row as hot-wallet outflow.
        Assert.Equal(EthereumHot, row.FromAddress);
    }

    [Theory]
    [InlineData(BlockchainTransactionStatus.PENDING)]
    [InlineData(BlockchainTransactionStatus.DETECTED)]
    public async Task T158f_Ethereum_BuyerRefund_While_The_Sweep_Is_In_Flight_Waits_Nothing_Sent_No_Retry_Spent(
        BlockchainTransactionStatus sweepStatus)
    {
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        _walletAddresses.EthereumHotWalletAddress = EthereumHot;
        var fixture = await SeedEthereumRefundAfterSweepAsync(BlockchainTransactionType.BUYER_REFUND, sweepStatus);

        await _sut.ExecuteAsync();

        // (A PENDING sweep is itself dispatched in the same run; the refund is not.)
        Assert.DoesNotContain(ethereum.Calls, c => c.BlockchainTransactionId == fixture.RefundRow.Id);
        var row = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == fixture.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, row.Status);
        Assert.Equal(0, row.RetryCount);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.Add(OutgoingTransferDispatchJob.DeferredRetryDelay), row.NextAttemptAt);
        Assert.StartsWith("SWEEP_IN_FLIGHT", row.ErrorMessage);
        Assert.Empty(_outbox.Events.OfType<TransferDispatchFailedEvent>());
    }

    [Fact]
    public async Task T158f_Ethereum_BuyerRefund_After_A_Failed_Sweep_Or_Without_One_Stays_On_The_Deposit()
    {
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        _walletAddresses.EthereumHotWalletAddress = EthereumHot;
        var failed = await SeedEthereumRefundAfterSweepAsync(
            BlockchainTransactionType.BUYER_REFUND, BlockchainTransactionStatus.FAILED);
        var none = await SeedEthereumRefundAfterSweepAsync(BlockchainTransactionType.BUYER_REFUND, null);

        await _sut.ExecuteAsync();

        Assert.Equal(2, ethereum.Calls.Count);
        Assert.All(ethereum.Calls, c =>
        {
            Assert.False(c.FromHotWallet);
            Assert.NotNull(c.DepositIndex);
        });
        Assert.Contains(ethereum.Calls, c => c.DepositAddress == failed.PaymentAddress.Address);
        Assert.Contains(ethereum.Calls, c => c.DepositAddress == none.PaymentAddress.Address);
    }

    [Theory]
    [InlineData(BlockchainTransactionType.EXCESS_REFUND)]
    [InlineData(BlockchainTransactionType.WRONG_TOKEN_REFUND)]
    [InlineData(BlockchainTransactionType.LATE_PAYMENT_REFUND)]
    public async Task T158f_Ethereum_Other_Refund_Types_Stay_On_The_Deposit_After_The_Sweep(
        BlockchainTransactionType type)
    {
        // The sweep moves the escrowed total only; an excess, a wrong token or
        // a late payment is still on the deposit after it.
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        _walletAddresses.EthereumHotWalletAddress = EthereumHot;
        var fixture = await SeedEthereumRefundAfterSweepAsync(type, BlockchainTransactionStatus.CONFIRMED);
        if (type == BlockchainTransactionType.WRONG_TOKEN_REFUND)
        {
            fixture.RefundRow.ActualTokenAddress = "0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48";
            await _db.SaveChangesAsync();
        }

        await _sut.ExecuteAsync();

        var call = Assert.Single(ethereum.Calls);
        Assert.False(call.FromHotWallet);
        Assert.Equal(fixture.PaymentAddress.Address, call.DepositAddress);
    }

    [Fact]
    public async Task T158f_Tron_BuyerRefund_After_A_Confirmed_Sweep_Is_Unchanged_Deposit_Sourced()
    {
        var fixture = await SeedRefundAsync(BlockchainTransactionType.BUYER_REFUND, amount: 99m);
        _db.Set<BlockchainTransaction>().Add(new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = fixture.Transaction.Id,
            PaymentAddressId = fixture.PaymentAddress.Id,
            Type = BlockchainTransactionType.SWEEP,
            TxHash = "tron-sweep-confirmed",
            FromAddress = fixture.PaymentAddress.Address,
            ToAddress = "THotWallet0000000000000000000000000",
            Amount = 100m,
            Token = StablecoinType.USDT,
            Status = BlockchainTransactionStatus.CONFIRMED,
            BlockNumber = 1_600_000L,
            ConfirmationCount = 20,
            ConfirmedAt = _clock.GetUtcNow().UtcDateTime,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync();

        await _sut.ExecuteAsync();

        var call = Assert.Single(_client.Calls);
        Assert.False(call.FromHotWallet);
        Assert.Equal(fixture.PaymentAddress.Address, call.DepositAddress);
    }

    [Fact]
    public async Task T158f_Hot_Wallet_Refund_Without_A_Configured_Hot_Wallet_Waits_Never_Failed()
    {
        var ethereum = new StubBlockchainTransferClient();
        _registry.With(BlockchainNetwork.ETHEREUM, transfers: ethereum);
        _walletAddresses.EthereumHotWalletAddress = null;
        var fixture = await SeedEthereumRefundAfterSweepAsync(
            BlockchainTransactionType.BUYER_REFUND, BlockchainTransactionStatus.CONFIRMED);

        await _sut.ExecuteAsync();

        Assert.Empty(ethereum.Calls);
        var row = await _db.Set<BlockchainTransaction>().AsNoTracking().FirstAsync(b => b.Id == fixture.RefundRow.Id);
        Assert.Equal(BlockchainTransactionStatus.PENDING, row.Status);
        Assert.Equal(0, row.RetryCount);
        Assert.StartsWith("HOT_WALLET_NOT_CONFIGURED", row.ErrorMessage);
    }

    [Theory]
    [InlineData(new[] { BlockchainTransactionStatus.CONFIRMED }, RefundSource.HotWallet)]
    [InlineData(new[] { BlockchainTransactionStatus.PENDING }, RefundSource.WaitForSweep)]
    [InlineData(new[] { BlockchainTransactionStatus.DETECTED }, RefundSource.WaitForSweep)]
    [InlineData(new[] { BlockchainTransactionStatus.FAILED }, RefundSource.Deposit)]
    [InlineData(new BlockchainTransactionStatus[0], RefundSource.Deposit)]
    [InlineData(new[] { BlockchainTransactionStatus.FAILED, BlockchainTransactionStatus.CONFIRMED }, RefundSource.HotWallet)]
    public void T158f_RefundSource_Rule(BlockchainTransactionStatus[] sweeps, RefundSource expected) =>
        Assert.Equal(expected, RefundSourceResolver.Decide(sweeps));

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

        /// <summary>Runs inside the broadcast, after the call is recorded (T169 S4-1: a mid-tick change).</summary>
        public Action<TransferBroadcastRequest>? OnBroadcast { get; set; }

        public Task<TransferBroadcastResult> BroadcastAsync(
            TransferBroadcastRequest request,
            CancellationToken cancellationToken)
        {
            Calls.Add(request);
            OnBroadcast?.Invoke(request);
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
