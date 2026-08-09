using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Reputation;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Application.Reputation;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Lifecycle;

/// <summary>
/// End-to-end coverage for <see cref="TransactionCancellationService"/>
/// (T51 — 07 §7.7, 02 §7, 03 §2.5 / §3.3). Verifies role-aware trigger
/// selection, post-payment guards, reason validation, item-return event
/// emission, reputation/cooldown side effects, and per-rejection error
/// codes.
/// </summary>
public class TransactionCancellationServiceTests : IntegrationTestBase
{
    static TransactionCancellationServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string ValidWallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";
    private const string SellerSteamId = "76561198000000200";
    private const string BuyerSteamId = "76561198000000201";

    private User _seller = null!;
    private User _buyer = null!;
    private FakeTimeProvider _clock = null!;
    private RecordingOutboxService _outbox = null!;
    private RecordingTimeoutScheduler _timeouts = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = SellerSteamId,
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = ValidWallet,
            MobileAuthenticatorVerified = true,
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = BuyerSteamId,
            SteamDisplayName = "Buyer",
            MobileAuthenticatorVerified = true,
        };
        context.Set<User>().AddRange(_seller, _buyer);
        await context.SaveChangesAsync();

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero));
        _outbox = new RecordingOutboxService();
        _timeouts = new RecordingTimeoutScheduler();
    }

    [Fact]
    public async Task Seller_Cancel_From_Created_Transitions_And_Emits_Cancel_Event()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.CREATED, withBuyer: false);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _seller.Id, tx.Id,
            new CancelTransactionRequest("Fiyatı yükseltmek istiyorum"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.Cancelled, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Equal(TransactionStatus.CANCELLED_SELLER, outcome.Body.Status);
        Assert.False(outcome.Body.PaymentRefunded);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.CANCELLED_SELLER, persisted.Status);
        Assert.Equal(CancelledByType.SELLER, persisted.CancelledBy);
        Assert.Equal("Fiyatı yükseltmek istiyorum", persisted.CancelReason);
        Assert.NotNull(persisted.CancelledAt);

        var cancelEvent = Assert.Single(_outbox.Published.OfType<TransactionCancelledEvent>());
        Assert.Equal(CancelledByType.SELLER, cancelEvent.CancelledBy);
        Assert.Equal(tx.Id, cancelEvent.TransactionId);
    }

    [Fact]
    public async Task Rejects_Cancel_When_Caller_Suspended()
    {
        // T105a — a suspended user cannot take the cancel action; their pending
        // steps fall to timeout instead and admin drives the lifecycle.
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED, withBuyer: true);
        var seller = await Context.Set<User>().SingleAsync(u => u.Id == _seller.Id);
        seller.IsSuspended = true;
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _seller.Id, tx.Id,
            new CancelTransactionRequest("İşlemi durdurmak istiyorum"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.AccountSuspended, outcome.Status);
        Assert.Equal(TransactionErrorCodes.AccountSuspended, outcome.ErrorCode);
        // Nothing moved — no state transition, no event.
        Assert.Empty(_outbox.Published);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, persisted.Status);
    }

    [Fact]
    public async Task Seller_Cancel_From_SellerConfirmed_Cancels_Without_Payment_Refund()
    {
        // v3.0 — the item never left the seller's inventory and the buyer has
        // not paid yet, so a cancel here moves nothing (02 §9).
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _seller.Id, tx.Id,
            new CancelTransactionRequest("İşlemi durdurmak istiyorum"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.Cancelled, outcome.Status);
        Assert.False(outcome.Body!.PaymentRefunded);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.CANCELLED_SELLER, persisted.Status);

        // Hangfire jobs cancelled exactly once.
        Assert.Equal(tx.Id, _timeouts.CancelledTransactions.Single());
    }

    [Fact]
    public async Task Buyer_Cancel_From_SellerConfirmed_Cancels_Without_Payment_Refund()
    {
        // The buyer may still walk away right up to the moment their money
        // lands in escrow (02 §7).
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _buyer.Id, tx.Id,
            new CancelTransactionRequest("Vazgeçtim, üzgünüm"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.Cancelled, outcome.Status);
        Assert.False(outcome.Body!.PaymentRefunded);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.CANCELLED_BUYER, persisted.Status);
        Assert.Equal(CancelledByType.BUYER, persisted.CancelledBy);
    }

    [Fact]
    public async Task Seller_Cancel_From_Accepted_Transitions_To_CancelledSeller()
    {
        // 05 §4.2 — ACCEPTED permits both SellerCancel and SellerDecline into
        // CANCELLED_SELLER; the service picks SellerCancel and the outcome is
        // the same terminal state either way.
        var tx = await CreateTransactionAsync(TransactionStatus.ACCEPTED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _seller.Id, tx.Id,
            new CancelTransactionRequest("Item göndermek istemiyorum"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.Cancelled, outcome.Status);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.CANCELLED_SELLER, persisted.Status);
        Assert.Equal(CancelledByType.SELLER, persisted.CancelledBy);
    }

    [Fact]
    public async Task Reason_Below_Minimum_Length_Returns_400_Validation()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.CREATED, withBuyer: false);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _seller.Id, tx.Id,
            new CancelTransactionRequest("kısa"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.ValidationFailed, outcome.Status);
        Assert.Equal(TransactionErrorCodes.CancelReasonRequired, outcome.ErrorCode);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.CREATED, persisted.Status);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task Reason_Whitespace_Padding_Counts_Trimmed_Length()
    {
        // 9 visible chars padded to 30 → trimmed length 9 → still rejected.
        var tx = await CreateTransactionAsync(TransactionStatus.CREATED, withBuyer: false);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _seller.Id, tx.Id,
            new CancelTransactionRequest("   abcdefghi          "),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.ValidationFailed, outcome.Status);
    }

    [Fact]
    public async Task Stranger_Caller_Returns_403_NotAParty()
    {
        var stranger = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000999",
            SteamDisplayName = "Stranger",
        };
        Context.Set<User>().Add(stranger);
        await Context.SaveChangesAsync();

        var tx = await CreateTransactionAsync(TransactionStatus.CREATED, withBuyer: false);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            stranger.Id, tx.Id,
            new CancelTransactionRequest("Bu işlem benim değil"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.NotAParty, outcome.Status);
        Assert.Equal(TransactionErrorCodes.NotAParty, outcome.ErrorCode);
    }

    [Fact]
    public async Task Transaction_Not_Found_Returns_404()
    {
        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _seller.Id, Guid.NewGuid(),
            new CancelTransactionRequest("Geçersiz işlem ID"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.NotFound, outcome.Status);
        Assert.Equal(TransactionErrorCodes.TransactionNotFound, outcome.ErrorCode);
    }

    [Theory]
    [InlineData(TransactionStatus.PAYMENT_RECEIVED)]
    [InlineData(TransactionStatus.ITEM_DELIVERED)]
    public async Task Post_Payment_Buyer_Cancel_Returns_422_PaymentAlreadySent(TransactionStatus status)
    {
        // 02 §7 / 07 §7.7 — once the buyer's money is in escrow the BUYER can no
        // longer cancel. v3.0 made this asymmetric, so the role matters: the
        // seller's path from PAYMENT_RECEIVED is covered separately below.
        var tx = await CreateTransactionAsync(status, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _buyer.Id, tx.Id,
            new CancelTransactionRequest("Geç kalan iptal denemesi"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.PaymentAlreadySent, outcome.Status);
        Assert.Equal(TransactionErrorCodes.PaymentAlreadySent, outcome.ErrorCode);

        // State must remain unchanged.
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(status, persisted.Status);
    }

    [Fact]
    public async Task Seller_Cancel_From_PaymentReceived_Succeeds_And_Refunds_Buyer()
    {
        // 02 §7 (v3.0) — the seller may still back out after the buyer paid.
        // Closing this path would not protect the buyer. A seller who wants out
        // just stops responding; the DeliveryDeadline then expires, and the
        // buyer waits longer for exactly the same refund.
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _seller.Id, tx.Id,
            new CancelTransactionRequest("Item'ı göndermekten vazgeçtim"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.Cancelled, outcome.Status);
        Assert.True(outcome.Body!.PaymentRefunded);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.CANCELLED_SELLER, persisted.Status);
        Assert.Equal(CancelledByType.SELLER, persisted.CancelledBy);
    }

    [Fact]
    public async Task Seller_Cancel_From_ItemDelivered_Returns_409_InvalidStateTransition()
    {
        // ITEM_DELIVERED has no seller-cancel edge (05 §4.2): the item is with
        // the buyer, so only settlement or an admin refund can move it.
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _seller.Id, tx.Id,
            new CancelTransactionRequest("Teslimattan sonra iptal denemesi"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.InvalidStateTransition, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InvalidStateTransition, outcome.ErrorCode);
    }

    [Theory]
    [InlineData(TransactionStatus.COMPLETED)]
    [InlineData(TransactionStatus.CANCELLED_SELLER)]
    [InlineData(TransactionStatus.CANCELLED_BUYER)]
    [InlineData(TransactionStatus.CANCELLED_TIMEOUT)]
    [InlineData(TransactionStatus.CANCELLED_ADMIN)]
    [InlineData(TransactionStatus.FLAGGED)]
    public async Task Terminal_Or_NonCancellable_State_Returns_409_InvalidStateTransition(TransactionStatus status)
    {
        var tx = await CreateTransactionAsync(status, withBuyer: status != TransactionStatus.FLAGGED);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _seller.Id, tx.Id,
            new CancelTransactionRequest("Terminal state üzerinde iptal"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.InvalidStateTransition, outcome.Status);
        Assert.Equal(TransactionErrorCodes.InvalidStateTransition, outcome.ErrorCode);
    }

    [Fact]
    public async Task Successful_Seller_Cancel_Recomputes_Reputation_For_Both_Parties()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ACCEPTED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _seller.Id, tx.Id,
            new CancelTransactionRequest("Reputation testi için iptal"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.Cancelled, outcome.Status);

        var persistedSeller = await Context.Set<User>().AsNoTracking().SingleAsync(u => u.Id == _seller.Id);
        var persistedBuyer = await Context.Set<User>().AsNoTracking().SingleAsync(u => u.Id == _buyer.Id);

        // Seller had one CANCELLED_SELLER → responsible cancel; rate denominator
        // = 1, numerator = 0 → 0.0000. Buyer was party but not responsible →
        // wash filter would attribute the row to seller; buyer's rate is null
        // (no responsible rows in their column).
        Assert.Equal(0, persistedSeller.CompletedTransactionCount);
        Assert.Equal(0m, persistedSeller.SuccessfulTransactionRate);

        Assert.Equal(0, persistedBuyer.CompletedTransactionCount);
        Assert.Null(persistedBuyer.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task Successful_Buyer_Cancel_Stamps_Cooldown_When_Threshold_Exceeded()
    {
        // Limit=1 means the cooldown trips when the buyer accumulates a
        // SECOND responsible cancel inside the window — seed one prior
        // CANCELLED_BUYER row, then drive a fresh cancel through the service.
        await Context.ConfigureSettingAsync("cancel_limit_count", "1");
        await Context.ConfigureSettingAsync("cancel_limit_period_hours", "24");
        await Context.ConfigureSettingAsync("cancel_cooldown_hours", "12");

        // Prior responsible cancel row (CANCELLED_BUYER, attributable to the
        // buyer) inside the rolling window.
        var priorTx = await CreateTransactionAsync(TransactionStatus.CANCELLED_BUYER, withBuyer: true);
        priorTx.CancelledAt = _clock.GetUtcNow().UtcDateTime.AddHours(-2);
        Context.Set<Transaction>().Update(priorTx);
        await Context.SaveChangesAsync();

        // The transaction the user actually cancels in this run.
        var tx = await CreateTransactionAsync(TransactionStatus.CREATED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _buyer.Id, tx.Id,
            new CancelTransactionRequest("Cooldown stamp testi için"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.Cancelled, outcome.Status);

        var persistedBuyer = await Context.Set<User>().AsNoTracking().SingleAsync(u => u.Id == _buyer.Id);
        Assert.NotNull(persistedBuyer.CooldownExpiresAt);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddHours(12), persistedBuyer.CooldownExpiresAt);
    }

    [Fact]
    public async Task Successful_Cancel_Cancels_Hangfire_Timeout_Jobs()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _seller.Id, tx.Id,
            new CancelTransactionRequest("Hangfire job iptal kontrolü"),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.Cancelled, outcome.Status);
        Assert.Single(_timeouts.CancelledTransactions);
        Assert.Equal(tx.Id, _timeouts.CancelledTransactions[0]);
    }

    [Fact]
    public async Task Successful_Cancel_Persists_Reason_And_CancelledAt_From_Clock()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.CREATED, withBuyer: false);
        var expectedAt = _clock.GetUtcNow().UtcDateTime;

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _seller.Id, tx.Id,
            new CancelTransactionRequest("   Padded reason text   "),
            CancellationToken.None);

        Assert.Equal(CancelTransactionStatus.Cancelled, outcome.Status);
        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        // Reason was trimmed during validation; the saved value omits padding.
        Assert.Equal("Padded reason text", persisted.CancelReason);
        // OnEntry uses DateTime.UtcNow (not the FakeTimeProvider) so we cannot
        // pin an exact value, but the response body mirrors the entity stamp.
        Assert.Equal(persisted.CancelledAt, outcome.Body!.CancelledAt);
    }

    private async Task<Transaction> CreateTransactionAsync(TransactionStatus status, bool withBuyer)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerId = withBuyer ? _buyer.Id : (Guid?)null,
            BuyerRefundAddress = withBuyer ? "TabcDEFGHJKLMNPQRSTUVWXYZ234567Xyz" : null,
            BuyerIdentificationMethod = withBuyer
                ? BuyerIdentificationMethod.STEAM_ID
                : BuyerIdentificationMethod.OPEN_LINK,
            TargetBuyerSteamId = withBuyer ? BuyerSteamId : null,
            InviteToken = withBuyer ? null : "T51-test-" + Guid.NewGuid().ToString("N")[..8],
            ItemAssetId = Guid.NewGuid().ToString("N")[..12],
            ItemClassId = "abc-class",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = ValidWallet,
            PaymentTimeoutMinutes = 1440,
            // Status-dependent caller-set field matrix (06 §3.5).
            AcceptedAt = status >= TransactionStatus.ACCEPTED && status != TransactionStatus.FLAGGED
                ? nowUtc.AddMinutes(-30) : null,
            SellerReadyConfirmedAt = status >= TransactionStatus.SELLER_CONFIRMED && status != TransactionStatus.FLAGGED
                ? nowUtc.AddMinutes(-25) : null,
            PaymentReceivedAt = status >= TransactionStatus.PAYMENT_RECEIVED && status != TransactionStatus.FLAGGED
                                 && status != TransactionStatus.CANCELLED_SELLER
                                 && status != TransactionStatus.CANCELLED_BUYER
                                 && status != TransactionStatus.CANCELLED_TIMEOUT
                                 && status != TransactionStatus.CANCELLED_ADMIN
                ? nowUtc.AddMinutes(-20) : null,
            ItemDeliveredAt = status == TransactionStatus.ITEM_DELIVERED || status == TransactionStatus.COMPLETED
                ? nowUtc.AddMinutes(-15) : null,
            DeliveredBuyerAssetId = status == TransactionStatus.ITEM_DELIVERED || status == TransactionStatus.COMPLETED
                ? "300400500" : null,
            CompletedAt = status == TransactionStatus.COMPLETED ? nowUtc.AddMinutes(-10) : null,
            // Cancelled state requires CancelledBy + CancelReason + CancelledAt.
            CancelledBy = status == TransactionStatus.CANCELLED_SELLER ? CancelledByType.SELLER
                : status == TransactionStatus.CANCELLED_BUYER ? CancelledByType.BUYER
                : status == TransactionStatus.CANCELLED_TIMEOUT ? CancelledByType.TIMEOUT
                : status == TransactionStatus.CANCELLED_ADMIN ? CancelledByType.ADMIN
                : (CancelledByType?)null,
            CancelReason = status >= TransactionStatus.CANCELLED_TIMEOUT
                ? "Pre-existing cancel reason for fixture" : null,
            CancelledAt = status >= TransactionStatus.CANCELLED_TIMEOUT ? nowUtc.AddMinutes(-5) : null,
            // FLAGGED has all deadlines NULL; CREATED needs AcceptDeadline.
            AcceptDeadline = status == TransactionStatus.CREATED ? nowUtc.AddHours(1) : null,
        };
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();
        return transaction;
    }

    private TransactionCancellationService BuildSut()
        => new(
            Context,
            _outbox,
            _timeouts,
            new ReputationAggregator(Context),
            new CancelCooldownEvaluator(Context, new SettingsBackedThresholdsProvider(Context), _clock),
            new Skinora.Transactions.Tests.Helpers.NoOpPostCancelMonitorStarter(),
            _clock);

    private sealed class RecordingOutboxService : IOutboxService
    {
        public List<IDomainEvent> Published { get; } = new();

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTimeoutScheduler : ITimeoutSchedulingService
    {
        public List<Guid> CancelledTransactions { get; } = new();

        public Task<TimeoutJobIds> SchedulePaymentTimeoutAsync(Guid transactionId, CancellationToken cancellationToken)
            => Task.FromResult(new TimeoutJobIds("job-" + transactionId.ToString("N")[..8], null));

        public Task CancelTimeoutJobsAsync(Guid transactionId, CancellationToken cancellationToken)
        {
            CancelledTransactions.Add(transactionId);
            return Task.CompletedTask;
        }

        public Task<TimeoutJobIds> ReschedulePaymentTimeoutAsync(
            Guid transactionId, TimeSpan remaining, DateTime newPaymentDeadlineUtc, CancellationToken cancellationToken)
            => Task.FromResult(new TimeoutJobIds("job-" + transactionId.ToString("N")[..8], null));
    }

    private sealed class SettingsBackedThresholdsProvider : ICancelCooldownThresholdsProvider
    {
        private readonly AppDbContext _db;
        public SettingsBackedThresholdsProvider(AppDbContext db) => _db = db;

        public async Task<CancelCooldownThresholds> GetAsync(CancellationToken cancellationToken)
        {
            var limit = await ReadIntAsync("cancel_limit_count", cancellationToken);
            var window = await ReadIntAsync("cancel_limit_period_hours", cancellationToken);
            var cooldown = await ReadIntAsync("cancel_cooldown_hours", cancellationToken);
            return new CancelCooldownThresholds(limit, window, cooldown);
        }

        private async Task<int> ReadIntAsync(string key, CancellationToken cancellationToken)
        {
            var raw = await _db.Set<Skinora.Platform.Domain.Entities.SystemSetting>()
                .AsNoTracking()
                .Where(s => s.Key == key && s.IsConfigured)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(cancellationToken);
            return int.TryParse(raw, out var v) ? v : 0;
        }
    }
}
