using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Reputation;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Application.Reputation;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Reputation;

/// <summary>
/// End-to-end coverage for <see cref="CancelCooldownEvaluator"/>: counts
/// responsible cancellations inside a rolling window and stamps
/// <c>User.CooldownExpiresAt</c> only when the configured limit is exceeded
/// (02 §14.2).
/// </summary>
public class CancelCooldownEvaluatorTests : IntegrationTestBase
{
    static CancelCooldownEvaluatorTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private User _seller = null!;
    private User _buyer = null!;
    private FakeTimeProvider _clock = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000000020", SteamDisplayName = "Seller" };
        _buyer = new User { Id = Guid.NewGuid(), SteamId = "76561198000000021", SteamDisplayName = "Buyer" };
        context.Set<User>().AddRange(_seller, _buyer);
        await context.SaveChangesAsync();

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Below_Limit_Leaves_CooldownExpiresAt_Untouched()
    {
        var thresholds = new StubThresholds(new CancelCooldownThresholds(LimitCount: 3, WindowHours: 24, CooldownHours: 12));
        var evaluator = new CancelCooldownEvaluator(Context, thresholds, _clock);

        // Two responsible cancellations within window — limit is 3.
        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_SELLER, hoursAgo: 5);
        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_SELLER, hoursAgo: 10);

        var result = await evaluator.EvaluateAsync(_seller.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(2, result.ResponsibleCancelCount);
        Assert.Null(result.NewCooldownExpiresAt);

        var seller = await Context.Set<User>().FindAsync(_seller.Id);
        Assert.Null(seller!.CooldownExpiresAt);
    }

    [Fact]
    public async Task Exceeding_Limit_Stamps_New_CooldownExpiresAt()
    {
        var thresholds = new StubThresholds(new CancelCooldownThresholds(LimitCount: 2, WindowHours: 24, CooldownHours: 12));
        var evaluator = new CancelCooldownEvaluator(Context, thresholds, _clock);

        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_SELLER, hoursAgo: 1);
        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_SELLER, hoursAgo: 5);
        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_SELLER, hoursAgo: 10);

        var result = await evaluator.EvaluateAsync(_seller.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(3, result.ResponsibleCancelCount);
        Assert.NotNull(result.NewCooldownExpiresAt);

        var seller = await Context.Set<User>().FindAsync(_seller.Id);
        var expectedExpiry = _clock.GetUtcNow().UtcDateTime.AddHours(12);
        Assert.Equal(expectedExpiry, seller!.CooldownExpiresAt);
    }

    [Fact]
    public async Task Cancellations_Outside_Window_Are_Ignored()
    {
        var thresholds = new StubThresholds(new CancelCooldownThresholds(LimitCount: 2, WindowHours: 24, CooldownHours: 12));
        var evaluator = new CancelCooldownEvaluator(Context, thresholds, _clock);

        // Three cancellations — but two are 30 hours ago, outside the 24h window.
        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_SELLER, hoursAgo: 30);
        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_SELLER, hoursAgo: 28);
        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_SELLER, hoursAgo: 10);

        var result = await evaluator.EvaluateAsync(_seller.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(1, result.ResponsibleCancelCount);
        Assert.Null(result.NewCooldownExpiresAt);
    }

    [Fact]
    public async Task Cancellations_For_Other_Party_Do_Not_Count()
    {
        var thresholds = new StubThresholds(new CancelCooldownThresholds(LimitCount: 2, WindowHours: 24, CooldownHours: 12));
        var evaluator = new CancelCooldownEvaluator(Context, thresholds, _clock);

        // Buyer-side cancellations should not count against the seller.
        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_BUYER, hoursAgo: 1);
        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_BUYER, hoursAgo: 5);
        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_BUYER, hoursAgo: 10);

        var result = await evaluator.EvaluateAsync(_seller.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(0, result.ResponsibleCancelCount);
    }

    [Fact]
    public async Task Disabled_Threshold_Returns_Zero_And_Skips_Update()
    {
        // Unconfigured row → provider returns 0 → rule must be a no-op even if
        // the user had thousands of cancellations.
        var thresholds = new StubThresholds(new CancelCooldownThresholds(LimitCount: 0, WindowHours: 0, CooldownHours: 0));
        var evaluator = new CancelCooldownEvaluator(Context, thresholds, _clock);

        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_SELLER, hoursAgo: 1);

        var result = await evaluator.EvaluateAsync(_seller.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(0, result.ResponsibleCancelCount);
        Assert.Null(result.NewCooldownExpiresAt);

        var seller = await Context.Set<User>().FindAsync(_seller.Id);
        Assert.Null(seller!.CooldownExpiresAt);
    }

    [Theory]
    // 06 §3.1 sorumluluk haritası — timeout'un düştüğü faz sorumluyu belirler.
    // Beklenen (satıcı sayımı, alıcı sayımı):
    [InlineData(TransactionStatus.CREATED, 0, 1)]           // adım 2 — alıcı kabul etmedi
    [InlineData(TransactionStatus.ACCEPTED, 1, 0)]          // adım 3 — satıcı hazırlık onayı vermedi
    [InlineData(TransactionStatus.SELLER_CONFIRMED, 0, 1)]  // adım 4 — alıcı ödemedi
    [InlineData(TransactionStatus.PAYMENT_RECEIVED, 1, 0)]  // adım 6–7 — satıcı teslim etmedi (v3.0)
    public async Task Timeout_Counts_Against_The_Phase_Owner_Only(
        TransactionStatus previousStatus,
        int expectedSellerCount,
        int expectedBuyerCount)
    {
        // The cooldown rule mirrors the reputation responsibility map. Without
        // this coverage the entire CANCELLED_TIMEOUT branch of the evaluator —
        // including the v3.0 delivery flip (PAYMENT_RECEIVED → seller) — rests
        // on the reputation aggregator's tests, which exercise a different
        // class.
        var thresholds = new StubThresholds(new CancelCooldownThresholds(LimitCount: 3, WindowHours: 24, CooldownHours: 12));
        var evaluator = new CancelCooldownEvaluator(Context, thresholds, _clock);

        var tx = await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_TIMEOUT, hoursAgo: 2);
        await InsertTimeoutHistoryAsync(tx.Id, previousStatus);

        var sellerResult = await evaluator.EvaluateAsync(_seller.Id, CancellationToken.None);
        var buyerResult = await evaluator.EvaluateAsync(_buyer.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(expectedSellerCount, sellerResult.ResponsibleCancelCount);
        Assert.Equal(expectedBuyerCount, buyerResult.ResponsibleCancelCount);
    }

    [Fact]
    public async Task Delivery_Timeouts_Push_The_Seller_Into_Cooldown()
    {
        // 02 §14.2 — non-delivery is the P2P model's primary abuse vector, so
        // repeated delivery timeouts must actually trip the cancel cooldown and
        // block the seller from opening new transactions.
        var thresholds = new StubThresholds(new CancelCooldownThresholds(LimitCount: 2, WindowHours: 24, CooldownHours: 12));
        var evaluator = new CancelCooldownEvaluator(Context, thresholds, _clock);

        foreach (var hoursAgo in new[] { 1, 5, 10 })
        {
            var tx = await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_TIMEOUT, hoursAgo);
            await InsertTimeoutHistoryAsync(tx.Id, TransactionStatus.PAYMENT_RECEIVED);
        }

        var sellerResult = await evaluator.EvaluateAsync(_seller.Id, CancellationToken.None);
        var buyerResult = await evaluator.EvaluateAsync(_buyer.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(3, sellerResult.ResponsibleCancelCount);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddHours(12), sellerResult.NewCooldownExpiresAt);

        var seller = await Context.Set<User>().FindAsync(_seller.Id);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime.AddHours(12), seller!.CooldownExpiresAt);

        // The buyer paid every time — they must stay clean.
        Assert.Equal(0, buyerResult.ResponsibleCancelCount);
        var buyer = await Context.Set<User>().FindAsync(_buyer.Id);
        Assert.Null(buyer!.CooldownExpiresAt);
    }

    [Fact]
    public async Task Timeout_Without_History_Row_Counts_For_Neither_Party()
    {
        // PreviousStatus is the only responsibility input (06 §3.1). A timeout
        // written without its history row is silently unattributable — pinned so
        // the dependency is visible instead of looking like a lenient rule.
        var thresholds = new StubThresholds(new CancelCooldownThresholds(LimitCount: 1, WindowHours: 24, CooldownHours: 12));
        var evaluator = new CancelCooldownEvaluator(Context, thresholds, _clock);

        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_TIMEOUT, hoursAgo: 1);
        await InsertCancellationAsync(_seller.Id, _buyer.Id, TransactionStatus.CANCELLED_TIMEOUT, hoursAgo: 3);

        var sellerResult = await evaluator.EvaluateAsync(_seller.Id, CancellationToken.None);
        var buyerResult = await evaluator.EvaluateAsync(_buyer.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(0, sellerResult.ResponsibleCancelCount);
        Assert.Equal(0, buyerResult.ResponsibleCancelCount);
        Assert.Null(sellerResult.NewCooldownExpiresAt);
    }

    // ---- helpers ----

    private async Task<Transaction> InsertCancellationAsync(
        Guid sellerId,
        Guid buyerId,
        TransactionStatus status,
        int hoursAgo)
    {
        var cancelledAt = _clock.GetUtcNow().UtcDateTime.AddHours(-hoursAgo);
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = sellerId,
            BuyerId = buyerId,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000099",
            ItemAssetId = Guid.NewGuid().ToString("N")[..12],
            ItemClassId = "1",
            ItemName = "Test Item",
            StablecoinType = StablecoinType.USDT,
            Price = 50m,
            CommissionRate = 0.02m,
            CommissionAmount = 1m,
            TotalAmount = 51m,
            SellerPayoutAddress = "TXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
            PaymentTimeoutMinutes = 60,
            CancelledAt = cancelledAt,
            // CK_Transactions_Cancel: cancel states need CancelledBy + CancelReason + CancelledAt.
            CancelledBy = status switch
            {
                TransactionStatus.CANCELLED_SELLER => CancelledByType.SELLER,
                TransactionStatus.CANCELLED_BUYER => CancelledByType.BUYER,
                TransactionStatus.CANCELLED_TIMEOUT => CancelledByType.TIMEOUT,
                TransactionStatus.CANCELLED_ADMIN => CancelledByType.ADMIN,
                _ => null
            },
            CancelReason = status is TransactionStatus.CANCELLED_SELLER
                                   or TransactionStatus.CANCELLED_BUYER
                                   or TransactionStatus.CANCELLED_TIMEOUT
                                   or TransactionStatus.CANCELLED_ADMIN
                           ? "test"
                           : null,
        };

        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        return tx;
    }

    /// <summary>
    /// Writes the <c>* → CANCELLED_TIMEOUT</c> audit row the evaluator reads to
    /// attribute a timeout (06 §3.1). Mirrors what <c>TimeoutExecutor</c> and
    /// <c>DeadlineScannerJob</c> record in production.
    /// </summary>
    private async Task InsertTimeoutHistoryAsync(Guid transactionId, TransactionStatus previousStatus)
    {
        var history = new TransactionHistory
        {
            TransactionId = transactionId,
            PreviousStatus = previousStatus,
            NewStatus = TransactionStatus.CANCELLED_TIMEOUT,
            Trigger = "test.timeout",
            ActorType = ActorType.SYSTEM,
            ActorId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };

        Context.Set<TransactionHistory>().Add(history);
        await Context.SaveChangesAsync();
    }

    private sealed class StubThresholds : ICancelCooldownThresholdsProvider
    {
        private readonly CancelCooldownThresholds _value;
        public StubThresholds(CancelCooldownThresholds value) => _value = value;
        public Task<CancelCooldownThresholds> GetAsync(CancellationToken _) => Task.FromResult(_value);
    }
}
