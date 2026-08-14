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
/// <see cref="DeadlineScannerJob"/> (T49 — 02 §3.2, 03 §4.1–§4.4). Walks every
/// scanner-driven phase: Accept and SellerConfirm (no refunds) and Delivery,
/// which since T124 emits nothing at all until the T127 verification round
/// exists. The Payment phase is the one leg driven by a per-transaction
/// Hangfire job rather than this scanner (05 §4.4); it is covered by
/// <see cref="TimeoutExecutorSideEffectsTests"/>.
/// </summary>
public class DeadlineScannerJobSideEffectsTests : IntegrationTestBase
{
    static DeadlineScannerJobSideEffectsTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private FakeTimeProvider _clock = null!;
    private CapturingJobScheduler _scheduler = null!;
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
        _scheduler = new CapturingJobScheduler();
        _outbox = new CapturingOutboxService();
    }

    private DeadlineScannerJob CreateSut() =>
        new(Context, _scheduler, _clock,
            new TimeoutSideEffectPublisher(_outbox, _clock, NullLogger<TimeoutSideEffectPublisher>.Instance),
            TimeoutTestFixtures.NoOpPostCancelMonitor(),
            TimeoutTestFixtures.NoOpReputationRefresher(),
            TimeoutTestFixtures.Options(),
            NullLogger<DeadlineScannerJob>.Instance);

    [Fact]
    public async Task Accept_Timeout_Publishes_Only_Notification_Event()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.CREATED, nowUtc,
            acceptDeadline: nowUtc.AddMinutes(-1));
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        await CreateSut().ScanAndRescheduleAsync();

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.CANCELLED_TIMEOUT, persisted.Status);

        var evt = Assert.IsType<TransactionTimedOutEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(TimeoutPhase.Accept, evt.Phase);

        Assert.Empty(_outbox.Published.OfType<PaymentRefundToBuyerRequestedEvent>());
        Assert.Empty(_outbox.Published.OfType<LatePaymentMonitorRequestedEvent>());
    }

    [Fact]
    public async Task SellerConfirm_Timeout_Publishes_Only_Notification_Event()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyer = await TimeoutTestFixtures.AddBuyerAsync(Context);
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ACCEPTED, nowUtc,
            sellerConfirmDeadline: nowUtc.AddMinutes(-1),
            buyerId: buyer.Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        await CreateSut().ScanAndRescheduleAsync();

        var evt = Assert.IsType<TransactionTimedOutEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(TimeoutPhase.SellerConfirm, evt.Phase);

    }

    /// <summary>
    /// T124 gate — the scanner does not reach the publisher for the delivery
    /// phase yet, so an overdue delivery emits NOTHING: no cancellation notice
    /// and, above all, no buyer refund. Publishing the refund event without the
    /// 05 §4.4 verification round would move money against a seller who may
    /// have delivered (02 §9.2); T127 adds that round and restores the
    /// expectations this test asserted before.
    /// </summary>
    /// <remarks>
    /// The publisher's own delivery-phase fan-out is unchanged and still
    /// covered directly by
    /// <see cref="TimeoutSideEffectPublisherTests.Delivery_Phase_Emits_Notification_And_PaymentRefund"/>
    /// — what this task severs is the scanner→publisher wiring, not the
    /// publisher.
    /// </remarks>
    [Fact]
    public async Task Delivery_Timeout_Publishes_Nothing_While_Gated_Until_T127()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var buyer = await TimeoutTestFixtures.AddBuyerAsync(Context);
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
            deliveryDeadline: nowUtc.AddMinutes(-1),
            buyerId: buyer.Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        await CreateSut().ScanAndRescheduleAsync();

        Assert.Empty(_outbox.Published);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);
    }
}
