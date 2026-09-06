using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Transfers;
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
    private readonly StubRetryPolicy _retryPolicy;
    private readonly CapturingOutboxService _outbox;
    private readonly FakeTimeProvider _clock;
    private readonly OutgoingTransferDispatchJob _sut;

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
        _retryPolicy = new StubRetryPolicy();
        _outbox = new CapturingOutboxService();
        _clock = new FakeTimeProvider();
        _clock.SetUtcNow(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        _sut = new OutgoingTransferDispatchJob(
            _db,
            _client,
            _retryPolicy,
            _outbox,
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

    // ÖLÇÜM (2026-09-06) — yanlış token iadesinde sidecar'a HANGİ token
    // söyleniyor? 06 §3.8 semantiği: `Token` beklenen stablecoin'i, depozit
    // adresinde fiilen duran yanlış token'ın kimliği `ActualTokenAddress`'i
    // taşır. Dispatcher broadcast'e `row.Token`'ı veriyor ve sidecar o sembolü
    // kontrata çeviriyor (RefundService.resolveContract). `ActualTokenAddress`
    // hiçbir alanda taşınmıyorsa, broadcast depozit adresinden onda BULUNMAYAN
    // bir token'ı istemiş olur. Bu test bugünkü davranışı sabitler: iki
    // assertion da BUGÜN geçer; ikincisi broadcast sözleşmesine gerçek
    // kontratı taşıyan bir alan eklendiği gün kırılır — testin yeniden
    // okunması gereken an tam olarak odur.
    [Fact]
    public async Task WrongTokenRefund_BroadcastCarriesExpectedToken_NotTheTokenOnTheDeposit()
    {
        var fixture = await SeedRefundAsync(
            BlockchainTransactionType.WRONG_TOKEN_REFUND, amount: 80m);
        Assert.Equal("TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8", fixture.RefundRow.ActualTokenAddress);

        await _sut.ExecuteAsync();

        var call = Assert.Single(_client.Calls);
        Assert.Equal(BlockchainTransactionType.WRONG_TOKEN_REFUND, call.Type);

        // Satırın BEKLENEN stablecoin'i — alıcının parasını fiilen tutan
        // kontrat değil.
        Assert.Equal(StablecoinType.USDT, call.Token);

        // Ve broadcast sözleşmesinde gerçek kontratı taşıyabilecek bir alan yok.
        var carriers = typeof(TransferBroadcastRequest).GetProperties()
            .Select(p => p.Name)
            .Where(n => n.Contains("Actual", StringComparison.Ordinal)
                || n.Contains("Contract", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(carriers);
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
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000801",
            SteamDisplayName = "Seller",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000802",
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
            HdWalletIndex = 7,
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

        public TransferStatusResult NextStatus { get; set; } =
            new(TransferStatusOutcome.Pending, null, 0, null, null);

        public List<TransferBroadcastRequest> Calls { get; } = new();
        public List<string> StatusCalls { get; } = new();

        public Task<TransferBroadcastResult> BroadcastAsync(
            TransferBroadcastRequest request,
            CancellationToken cancellationToken)
        {
            Calls.Add(request);
            return Task.FromResult(NextResult);
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
