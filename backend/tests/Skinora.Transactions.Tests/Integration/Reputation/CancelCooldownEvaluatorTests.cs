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

    // ---- helpers ----

    private async Task InsertCancellationAsync(
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
            ItemAssetId = "1",
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
    }

    private sealed class StubThresholds : ICancelCooldownThresholdsProvider
    {
        private readonly CancelCooldownThresholds _value;
        public StubThresholds(CancelCooldownThresholds value) => _value = value;
        public Task<CancelCooldownThresholds> GetAsync(CancellationToken _) => Task.FromResult(_value);
    }
}
