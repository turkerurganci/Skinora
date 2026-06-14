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
    private readonly CapturingOutboxService _outbox;
    private readonly FakeTimeProvider _clock;
    private readonly OutgoingTransferConfirmationJob _sut;

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
        _outbox = new CapturingOutboxService();
        _clock = new FakeTimeProvider();
        _clock.SetUtcNow(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        _sut = new OutgoingTransferConfirmationJob(
            _db,
            _client,
            _outbox,
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
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000711",
            SteamDisplayName = "Seller",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000712",
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

        // Inbound rows need a PaymentAddress to satisfy the CK_*_BuyerPayment constraint.
        Guid? paymentAddressId = null;
        if (type == BlockchainTransactionType.BUYER_PAYMENT)
        {
            var pa = new PaymentAddress
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                Address = "TDeposit" + Guid.NewGuid().ToString("N").Substring(0, 26),
                HdWalletIndex = 1,
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

    private sealed class StubBlockchainTransferClient : IBlockchainTransferClient
    {
        public TransferStatusResult NextStatus { get; set; } =
            new(TransferStatusOutcome.Pending, null, 0, null, null);

        public List<string> StatusCalls { get; } = new();

        public Task<TransferBroadcastResult> BroadcastAsync(
            TransferBroadcastRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Confirmation job should not call Broadcast.");

        public Task<TransferStatusResult> GetStatusAsync(
            string txHash, CancellationToken cancellationToken)
        {
            StatusCalls.Add(txHash);
            return Task.FromResult(NextStatus);
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
