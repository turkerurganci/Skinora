using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.GasFee;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Application.Webhooks;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.Webhooks;

/// <summary>
/// Unit coverage for <see cref="AmountValidationService"/> (T72 — 02 §4.4,
/// 08 §3.4). Exercises every classification branch against a SQLite
/// in-memory DbContext, with stubbed gas-fee settings / outbox so the
/// assertions can focus on the orchestration contract (state-machine fire,
/// refund-intent persistence, outbox event shape).
/// </summary>
[Trait("Category", "Unit")]
public sealed class AmountValidationServiceTests : IDisposable
{
    static AmountValidationServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly StubGasFeeSettingsProvider _settings;
    private readonly StubRefundBlockedAlertService _alerts;
    private readonly CapturingOutboxService _outbox;
    private readonly RecordingTimeoutScheduling _timeoutScheduling;
    private readonly FakeTimeProvider _clock;
    private readonly AmountValidationService _sut;

    public AmountValidationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _settings = new StubGasFeeSettingsProvider
        {
            Settings = new GasFeeSettings(
                ProtectionRatio: 0.10m,
                MinRefundThresholdRatio: 2m,
                RefundGasFeeEstimateUsdt: 2m,
                PayoutGasFeeEstimateUsdt: 0.50m),
        };
        _alerts = new StubRefundBlockedAlertService();
        _outbox = new CapturingOutboxService();
        _timeoutScheduling = new RecordingTimeoutScheduling();
        _clock = new FakeTimeProvider();
        _clock.SetUtcNow(new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero));

        var decisionService = new RefundDecisionService(_settings);

        _sut = new AmountValidationService(
            _db,
            _settings,
            decisionService,
            _alerts,
            _outbox,
            _timeoutScheduling,
            _clock,
            NullLogger<AmountValidationService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // WP16 — records CancelTimeoutJobsAsync so tests can assert the payment-timeout
    // job is cancelled when (and only when) the payment confirmation advances the
    // state machine to PAYMENT_RECEIVED.
    private sealed class RecordingTimeoutScheduling : ITimeoutSchedulingService
    {
        public List<Guid> Cancelled { get; } = [];

        public Task<TimeoutJobIds> SchedulePaymentTimeoutAsync(Guid transactionId, CancellationToken cancellationToken)
            => Task.FromResult(new TimeoutJobIds("p", "w"));

        public Task CancelTimeoutJobsAsync(Guid transactionId, CancellationToken cancellationToken)
        {
            Cancelled.Add(transactionId);
            return Task.CompletedTask;
        }

        public Task<TimeoutJobIds> ReschedulePaymentTimeoutAsync(
            Guid transactionId, TimeSpan remaining, DateTime newPaymentDeadlineUtc, CancellationToken cancellationToken)
            => Task.FromResult(new TimeoutJobIds("p", "w"));
    }

    // ─── Correct amount ─────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmedPayment_ExactAmount_FiresConfirmPayment_PublishesPaymentReceivedEvent()
    {
        var fixture = await SeedAsync(expectedAmount: 100m, receivedAmount: 100m);

        var outcome = await _sut.ValidateConfirmedBuyerPaymentAsync(fixture.BlockchainTransaction, "corr-1", default);
        await _db.SaveChangesAsync();

        Assert.Equal(AmountValidationOutcome.AcceptedExact, outcome);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, fixture.Transaction.Status);
        Assert.NotNull(fixture.Transaction.PaymentReceivedAt);
        Assert.Single(_outbox.Events.OfType<PaymentReceivedEvent>());
        // WP16 — payment arrived → the per-tx payment-timeout job is cancelled.
        Assert.Contains(fixture.Transaction.Id, _timeoutScheduling.Cancelled);
        Assert.Empty(_outbox.Events.OfType<BuyerPaymentInsufficientEvent>());
        Assert.Empty(_outbox.Events.OfType<BuyerPaymentExcessRefundedEvent>());
        // No refund row written when amount is exact.
        var refundRows = await _db.Set<BlockchainTransaction>()
            .Where(b => b.Type != BlockchainTransactionType.BUYER_PAYMENT)
            .CountAsync();
        Assert.Equal(0, refundRows);
    }

    // ─── Underpayment ───────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmedPayment_Underpayment_AboveThreshold_QueuesIncorrectAmountRefund()
    {
        var fixture = await SeedAsync(expectedAmount: 100m, receivedAmount: 50m);

        var outcome = await _sut.ValidateConfirmedBuyerPaymentAsync(fixture.BlockchainTransaction, "corr-u1", default);
        await _db.SaveChangesAsync();

        Assert.Equal(AmountValidationOutcome.Underpaid, outcome);
        // State machine MUST NOT advance — timeout continues per 02 §4.4.
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, fixture.Transaction.Status);
        // WP16 — the payment-timeout job stays armed (no advance → no cancel).
        Assert.DoesNotContain(fixture.Transaction.Id, _timeoutScheduling.Cancelled);
        var refund = await _db.Set<BlockchainTransaction>()
            .SingleAsync(b => b.Type == BlockchainTransactionType.INCORRECT_AMOUNT_REFUND);
        Assert.Equal(BlockchainTransactionStatus.PENDING, refund.Status);
        Assert.Equal(fixture.BlockchainTransaction.FromAddress, refund.ToAddress);
        Assert.Equal(50m, refund.Amount);
        Assert.Null(refund.PaymentAddressId);

        var domainEvent = Assert.Single(_outbox.Events.OfType<BuyerPaymentInsufficientEvent>());
        Assert.Equal(fixture.Transaction.Id, domainEvent.TransactionId);
        Assert.Equal(fixture.Transaction.BuyerId, domainEvent.BuyerId);
        Assert.Equal(refund.Id, domainEvent.RefundTransactionId);
        Assert.Equal(100m, domainEvent.ExpectedAmount);
        Assert.Equal(50m, domainEvent.ReceivedAmount);
        Assert.Equal(fixture.BlockchainTransaction.FromAddress, domainEvent.SourceAddress);
        Assert.Empty(_alerts.Raised);
    }

    [Fact]
    public async Task ConfirmedPayment_Underpayment_BelowThreshold_RaisesAdminAlertAndSkipsRefund()
    {
        // received - gasFee = 3 - 2 = 1 ; threshold = gasFee × 2 = 4 ⇒ block.
        var fixture = await SeedAsync(expectedAmount: 100m, receivedAmount: 3m);

        var outcome = await _sut.ValidateConfirmedBuyerPaymentAsync(fixture.BlockchainTransaction, "corr-u2", default);
        await _db.SaveChangesAsync();

        Assert.Equal(AmountValidationOutcome.Underpaid, outcome);
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, fixture.Transaction.Status);
        var refundCount = await _db.Set<BlockchainTransaction>()
            .CountAsync(b => b.Type == BlockchainTransactionType.INCORRECT_AMOUNT_REFUND);
        Assert.Equal(0, refundCount);
        Assert.Empty(_outbox.Events.OfType<BuyerPaymentInsufficientEvent>());
        var alert = Assert.Single(_alerts.Raised);
        Assert.Equal(fixture.Transaction.Id, alert.TransactionId);
        Assert.Equal(RefundBlockedReason.BelowMinimumThreshold, alert.Decision.Reason);
    }

    // ─── Overpayment ────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmedPayment_Overpayment_AdvancesStateAndQueuesExcessRefund()
    {
        var fixture = await SeedAsync(expectedAmount: 100m, receivedAmount: 110m);

        var outcome = await _sut.ValidateConfirmedBuyerPaymentAsync(fixture.BlockchainTransaction, "corr-o1", default);
        await _db.SaveChangesAsync();

        Assert.Equal(AmountValidationOutcome.AcceptedWithExcessRefund, outcome);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, fixture.Transaction.Status);
        var refund = await _db.Set<BlockchainTransaction>()
            .SingleAsync(b => b.Type == BlockchainTransactionType.EXCESS_REFUND);
        Assert.Equal(10m, refund.Amount);
        Assert.Equal(BlockchainTransactionStatus.PENDING, refund.Status);
        Assert.Equal(fixture.BlockchainTransaction.FromAddress, refund.ToAddress);

        Assert.Single(_outbox.Events.OfType<PaymentReceivedEvent>());
        var domainEvent = Assert.Single(_outbox.Events.OfType<BuyerPaymentExcessRefundedEvent>());
        Assert.False(domainEvent.IsMultiPayment);
        Assert.Equal(10m, domainEvent.ExcessAmount);
        Assert.Equal(100m, domainEvent.ExpectedAmount);
        Assert.Equal(110m, domainEvent.ReceivedAmount);
        Assert.Empty(_alerts.Raised);
    }

    [Fact]
    public async Task ConfirmedPayment_Overpayment_ExcessBelowThreshold_AdvancesStateButRaisesAlertOnly()
    {
        // received - expected = 0.5; net = 0.5 - 2 = -1.5 ⇒ NegativeAmount block.
        var fixture = await SeedAsync(expectedAmount: 100m, receivedAmount: 100.5m);

        var outcome = await _sut.ValidateConfirmedBuyerPaymentAsync(fixture.BlockchainTransaction, "corr-o2", default);
        await _db.SaveChangesAsync();

        Assert.Equal(AmountValidationOutcome.AcceptedWithExcessRefund, outcome);
        // State machine still advances — the platform has the expected amount.
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, fixture.Transaction.Status);
        var refundCount = await _db.Set<BlockchainTransaction>()
            .CountAsync(b => b.Type == BlockchainTransactionType.EXCESS_REFUND);
        Assert.Equal(0, refundCount);
        Assert.Single(_outbox.Events.OfType<PaymentReceivedEvent>());
        Assert.Empty(_outbox.Events.OfType<BuyerPaymentExcessRefundedEvent>());
        var alert = Assert.Single(_alerts.Raised);
        Assert.Equal(RefundBlockedReason.NegativeAmount, alert.Decision.Reason);
    }

    // ─── Multi-payment ──────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmedPayment_MultiPayment_PostEscrowState_RefundsEntireAmount()
    {
        // Seed the transaction past ITEM_ESCROWED (PAYMENT_RECEIVED) — a stray
        // confirmation after the buyer has already paid in full is the
        // multi-payment scenario in 02 §4.4.
        var fixture = await SeedAsync(
            expectedAmount: 100m,
            receivedAmount: 100m,
            initialStatus: TransactionStatus.PAYMENT_RECEIVED);

        var outcome = await _sut.ValidateConfirmedBuyerPaymentAsync(fixture.BlockchainTransaction, "corr-m1", default);
        await _db.SaveChangesAsync();

        Assert.Equal(AmountValidationOutcome.MultiPaymentRefunded, outcome);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, fixture.Transaction.Status); // unchanged
        var refund = await _db.Set<BlockchainTransaction>()
            .SingleAsync(b => b.Type == BlockchainTransactionType.EXCESS_REFUND);
        Assert.Equal(100m, refund.Amount); // full received amount
        var domainEvent = Assert.Single(_outbox.Events.OfType<BuyerPaymentExcessRefundedEvent>());
        Assert.True(domainEvent.IsMultiPayment);
        Assert.Equal(100m, domainEvent.ExcessAmount);
        Assert.Empty(_outbox.Events.OfType<PaymentReceivedEvent>());
    }

    // ─── Wrong-token ────────────────────────────────────────────────────

    [Fact]
    public async Task WrongTokenIncoming_AboveThreshold_QueuesWrongTokenRefund()
    {
        var fixture = await SeedAsync(expectedAmount: 100m, receivedAmount: 0m); // BUYER_PAYMENT not used here
        var wrongTokenRow = await SeedWrongTokenIncomingAsync(
            fixture,
            amount: 50m,
            actualContract: KnownStablecoinContractsForTest.Usdc);

        var outcome = await _sut.ValidateWrongTokenIncomingAsync(wrongTokenRow, "corr-w1", default);
        await _db.SaveChangesAsync();

        Assert.Equal(AmountValidationOutcome.WrongTokenRefundQueued, outcome);
        var refund = await _db.Set<BlockchainTransaction>()
            .SingleAsync(b => b.Type == BlockchainTransactionType.WRONG_TOKEN_REFUND);
        Assert.Equal(50m, refund.Amount);
        Assert.Equal(BlockchainTransactionStatus.PENDING, refund.Status);
        Assert.Equal(wrongTokenRow.FromAddress, refund.ToAddress);
        Assert.Equal(KnownStablecoinContractsForTest.Usdc, refund.ActualTokenAddress);
        // 06 §3.8 token semantiği — refund row carries the *expected* stablecoin.
        Assert.Equal(StablecoinType.USDT, refund.Token);

        var domainEvent = Assert.Single(_outbox.Events.OfType<WrongTokenRefundRequestedEvent>());
        Assert.Equal(StablecoinType.USDT, domainEvent.ExpectedStablecoin);
        Assert.Equal(StablecoinType.USDC, domainEvent.ActualStablecoin);
        Assert.Equal(refund.Id, domainEvent.RefundTransactionId);
        Assert.Empty(_alerts.Raised);
    }

    [Fact]
    public async Task WrongTokenIncoming_BelowThreshold_RaisesAdminAlertOnly()
    {
        var fixture = await SeedAsync(expectedAmount: 100m, receivedAmount: 0m);
        var wrongTokenRow = await SeedWrongTokenIncomingAsync(
            fixture,
            amount: 3m,
            actualContract: KnownStablecoinContractsForTest.Usdc);

        var outcome = await _sut.ValidateWrongTokenIncomingAsync(wrongTokenRow, "corr-w2", default);
        await _db.SaveChangesAsync();

        Assert.Equal(AmountValidationOutcome.WrongTokenAdminAlert, outcome);
        var refundCount = await _db.Set<BlockchainTransaction>()
            .CountAsync(b => b.Type == BlockchainTransactionType.WRONG_TOKEN_REFUND);
        Assert.Equal(0, refundCount);
        Assert.Empty(_outbox.Events.OfType<WrongTokenRefundRequestedEvent>());
        Assert.Single(_alerts.Raised);
    }

    // ─── State machine refused ──────────────────────────────────────────

    [Fact]
    public async Task ConfirmedPayment_OnEmergencyHold_DoesNotAdvanceOrRefund()
    {
        var fixture = await SeedAsync(
            expectedAmount: 100m,
            receivedAmount: 100m,
            isOnHold: true);

        var outcome = await _sut.ValidateConfirmedBuyerPaymentAsync(fixture.BlockchainTransaction, "corr-h1", default);
        await _db.SaveChangesAsync();

        Assert.Equal(AmountValidationOutcome.StateMachineRejected, outcome);
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, fixture.Transaction.Status);
        Assert.Empty(_outbox.Events.OfType<PaymentReceivedEvent>());
        var refundCount = await _db.Set<BlockchainTransaction>()
            .CountAsync(b => b.Type != BlockchainTransactionType.BUYER_PAYMENT);
        Assert.Equal(0, refundCount);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private async Task<Fixture> SeedAsync(
        decimal expectedAmount,
        decimal receivedAmount,
        TransactionStatus initialStatus = TransactionStatus.SELLER_CONFIRMED,
        bool isOnHold = false)
    {
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000401",
            SteamDisplayName = "Seller",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000402",
            SteamDisplayName = "Buyer",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        var admin = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000403",
            SteamDisplayName = "Admin",
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<User>().AddRange(seller, buyer, admin);
        var sellerId = seller.Id;
        var buyerId = buyer.Id;

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = initialStatus,
            SellerId = sellerId,
            BuyerId = buyerId,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000301",
            BuyerRefundAddress = "TBuyerRefund000000000000000000000000",
            ItemAssetId = "asset-1",
            ItemClassId = "cls",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = expectedAmount - 2m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = expectedAmount,
            SellerPayoutAddress = "TSellerPayout00000000000000000000000",
            IsOnHold = isOnHold,
        };

        if (isOnHold)
        {
            // CK_Transactions_FreezeHold_Reverse: IsOnHold=1 requires
            // TimeoutFrozenAt + TimeoutFreezeReason=EMERGENCY_HOLD (06 §3.5).
            tx.EmergencyHoldAt = _clock.GetUtcNow().UtcDateTime;
            tx.EmergencyHoldReason = "test hold";
            tx.EmergencyHoldByAdminId = admin.Id;
            tx.PreviousStatusBeforeHold = (int)initialStatus;
            tx.TimeoutFrozenAt = _clock.GetUtcNow().UtcDateTime;
            tx.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
            tx.TimeoutRemainingSeconds = 600;
        }

        var paymentAddress = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            Address = "TDeposit" + Guid.NewGuid().ToString("N").Substring(0, 26),
            HdWalletIndex = 1,
            ExpectedAmount = expectedAmount,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };

        var blockchainTx = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            PaymentAddressId = paymentAddress.Id,
            Type = BlockchainTransactionType.BUYER_PAYMENT,
            TxHash = "0xTest" + Guid.NewGuid().ToString("N"),
            FromAddress = "TBuyerSource0000000000000000000000000",
            ToAddress = paymentAddress.Address,
            Amount = receivedAmount,
            Token = StablecoinType.USDT,
            Status = BlockchainTransactionStatus.CONFIRMED,
            BlockNumber = 1_500_000L,
            ConfirmationCount = 20,
            ConfirmedAt = _clock.GetUtcNow().UtcDateTime,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };

        _db.Set<Transaction>().Add(tx);
        _db.Set<PaymentAddress>().Add(paymentAddress);
        _db.Set<BlockchainTransaction>().Add(blockchainTx);
        await _db.SaveChangesAsync();

        return new Fixture(tx, paymentAddress, blockchainTx);
    }

    private async Task<BlockchainTransaction> SeedWrongTokenIncomingAsync(
        Fixture fixture,
        decimal amount,
        string actualContract)
    {
        var row = new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = fixture.Transaction.Id,
            PaymentAddressId = fixture.PaymentAddress.Id,
            Type = BlockchainTransactionType.WRONG_TOKEN_INCOMING,
            TxHash = "0xWrong" + Guid.NewGuid().ToString("N"),
            FromAddress = "TBuyerSource0000000000000000000000000",
            ToAddress = fixture.PaymentAddress.Address,
            Amount = amount,
            Token = StablecoinType.USDT, // expected per 06 §3.8 token semantiği
            ActualTokenAddress = actualContract,
            Status = BlockchainTransactionStatus.DETECTED,
            BlockNumber = null,
            ConfirmationCount = 0,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Set<BlockchainTransaction>().Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private sealed record Fixture(
        Transaction Transaction,
        PaymentAddress PaymentAddress,
        BlockchainTransaction BlockchainTransaction);

    private sealed class StubGasFeeSettingsProvider : IGasFeeSettingsProvider
    {
        public GasFeeSettings Settings { get; init; } =
            new(ProtectionRatio: 0.10m, MinRefundThresholdRatio: 2m, RefundGasFeeEstimateUsdt: 2m, PayoutGasFeeEstimateUsdt: 0.50m);

        public Task<GasFeeSettings> GetAsync(CancellationToken cancellationToken)
            => Task.FromResult(Settings);
    }

    private sealed class StubRefundBlockedAlertService : IRefundBlockedAlertService
    {
        public List<(Guid TransactionId, RefundDecision Decision)> Raised { get; } = new();

        public Task RaiseAsync(Guid transactionId, RefundDecision decision, CancellationToken cancellationToken)
        {
            Raised.Add((transactionId, decision));
            return Task.CompletedTask;
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

    /// <summary>
    /// MVP allowlist mirrored from <c>KnownStablecoinContracts</c>
    /// (internal to Skinora.Transactions). Tests use the same address values
    /// without taking the InternalsVisibleTo dependency.
    /// </summary>
    private static class KnownStablecoinContractsForTest
    {
        public const string Usdt = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";
        public const string Usdc = "TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8";
    }
}
