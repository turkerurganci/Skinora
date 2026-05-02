using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Timeouts;

/// <summary>
/// Integration coverage for the side-effect fan-out wired into
/// <see cref="TimeoutExecutor"/> (T49 — 02 §3.2, 03 §4.3). The executor only
/// targets <c>ITEM_ESCROWED</c> (the per-tx Hangfire job covers the payment
/// stage — 05 §4.4), so the Payment phase side-effect set is the entire
/// surface here.
/// </summary>
public class TimeoutExecutorSideEffectsTests : IntegrationTestBase
{
    static TimeoutExecutorSideEffectsTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private FakeTimeProvider _clock = null!;
    private CapturingOutboxService _outbox = null!;
    private User _seller = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = TimeoutTestFixtures.SellerSteamId,
            SteamDisplayName = "Seller",
        };
        context.Set<User>().Add(_seller);
        await context.SaveChangesAsync();
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero));
        _outbox = new CapturingOutboxService();
    }

    private TimeoutExecutor CreateSut() =>
        new(Context, _clock,
            new TimeoutSideEffectPublisher(_outbox, _clock, NullLogger<TimeoutSideEffectPublisher>.Instance),
            NullLogger<TimeoutExecutor>.Instance);

    [Fact]
    public async Task Payment_Timeout_Publishes_Notification_ItemRefund_And_LatePaymentMonitor()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyer = await TimeoutTestFixtures.AddBuyerAsync(Context);
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(-1),
            buyerId: buyer.Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.EscrowBotAssetId = "100200300-bot";
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        await CreateSut().ExecutePaymentTimeoutAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, persisted.Status);

        Assert.Equal(3, _outbox.Published.Count);
        var notify = Assert.Single(_outbox.Published.OfType<TransactionTimedOutEvent>());
        Assert.Equal(TimeoutPhase.Payment, notify.Phase);
        Assert.Equal(buyer.Id, notify.BuyerId);

        Assert.Single(_outbox.Published.OfType<ItemRefundToSellerRequestedEvent>());
        Assert.Single(_outbox.Published.OfType<LatePaymentMonitorRequestedEvent>());
    }

    [Fact]
    public async Task Payment_Timeout_NoOp_Does_Not_Publish_When_State_Already_Advanced()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyer = await TimeoutTestFixtures.AddBuyerAsync(Context);
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(-1),
            buyerId: buyer.Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.EscrowBotAssetId = "100200300-bot";
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        await CreateSut().ExecutePaymentTimeoutAsync(transaction.Id);

        Assert.Empty(_outbox.Published);
    }
}
