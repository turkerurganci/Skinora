using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Timeouts;

/// <summary>
/// Integration coverage for <see cref="WarningDispatcher"/> (T48 — 02 §3.4,
/// 05 §4.4, 09 §13.3). The dispatcher must atomically stamp
/// <see cref="Transaction.TimeoutWarningSentAt"/> and publish a
/// <see cref="TimeoutWarningEvent"/>; every guard miss is a silent no-op so a
/// stale Hangfire job (state advanced, freeze/hold engaged, deadline passed,
/// already sent) cannot produce a misleading "süre dolmak üzere" notification.
/// </summary>
public class WarningDispatcherTests : IntegrationTestBase
{
    static WarningDispatcherTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private FakeTimeProvider _clock = null!;
    private RecordingOutboxService _outbox = null!;
    private User _seller = null!;
    private User _buyer = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = TimeoutTestFixtures.SellerSteamId,
            SteamDisplayName = "Seller",
        };
        context.Set<User>().Add(_seller);
        _buyer = await TimeoutTestFixtures.AddBuyerAsync(context);

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero));
        _outbox = new RecordingOutboxService();
    }

    [Fact]
    public async Task DispatchWarning_Stamps_SentAt_And_Publishes_Event()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = NewEscrowedTransaction(paymentDeadline: nowUtc.AddMinutes(15));
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        await BuildSut().DispatchWarningAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>()
            .AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(nowUtc, persisted.TimeoutWarningSentAt);

        var published = Assert.IsType<TimeoutWarningEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(transaction.Id, published.TransactionId);
        Assert.Equal(_buyer.Id, published.RecipientUserId);
        Assert.Equal(transaction.ItemName, published.ItemName);
        Assert.Equal(15, published.RemainingMinutes);
        Assert.Equal(nowUtc, published.OccurredAt);
        Assert.NotEqual(Guid.Empty, published.EventId);
    }

    [Fact]
    public async Task DispatchWarning_NoOp_When_Already_Sent()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var sentAt = nowUtc.AddMinutes(-10);
        var transaction = NewEscrowedTransaction(paymentDeadline: nowUtc.AddMinutes(15));
        transaction.TimeoutWarningSentAt = sentAt;
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        await BuildSut().DispatchWarningAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>()
            .AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Equal(sentAt, persisted.TimeoutWarningSentAt);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task DispatchWarning_NoOp_When_State_Advanced()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.PAYMENT_RECEIVED, nowUtc,
            paymentDeadline: nowUtc.AddMinutes(15),
            buyerId: _buyer.Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.EscrowBotAssetId = "100200300-bot";
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        await BuildSut().DispatchWarningAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>()
            .AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(persisted.TimeoutWarningSentAt);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task DispatchWarning_NoOp_When_Frozen()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = NewEscrowedTransaction(paymentDeadline: nowUtc.AddMinutes(15));
        transaction.TimeoutFrozenAt = nowUtc.AddMinutes(-5);
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.MAINTENANCE;
        transaction.TimeoutRemainingSeconds = 600;
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        await BuildSut().DispatchWarningAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>()
            .AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(persisted.TimeoutWarningSentAt);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task DispatchWarning_NoOp_When_OnHold()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = NewEscrowedTransaction(paymentDeadline: nowUtc.AddMinutes(15));
        transaction.IsOnHold = true;
        transaction.TimeoutFrozenAt = nowUtc.AddMinutes(-5);
        transaction.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        transaction.TimeoutRemainingSeconds = 600;
        transaction.EmergencyHoldAt = nowUtc.AddMinutes(-5);
        transaction.EmergencyHoldReason = "test";
        transaction.EmergencyHoldByAdminId = _seller.Id;
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        await BuildSut().DispatchWarningAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>()
            .AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(persisted.TimeoutWarningSentAt);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task DispatchWarning_NoOp_When_Deadline_Already_Passed()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var transaction = NewEscrowedTransaction(paymentDeadline: nowUtc.AddMinutes(-1));
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        await BuildSut().DispatchWarningAsync(transaction.Id);

        var persisted = await Context.Set<Transaction>()
            .AsNoTracking().SingleAsync(t => t.Id == transaction.Id);
        Assert.Null(persisted.TimeoutWarningSentAt);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task DispatchWarning_NoOp_When_Transaction_Missing()
    {
        await BuildSut().DispatchWarningAsync(Guid.NewGuid());
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task DispatchWarning_RemainingMinutes_Floor_When_Subminute_Slack()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        // 14 min 45 sec → floor to 14 minutes (truncation per Math.Floor)
        var transaction = NewEscrowedTransaction(
            paymentDeadline: nowUtc.AddMinutes(14).AddSeconds(45));
        Context.Set<Transaction>().Add(transaction);
        await Context.SaveChangesAsync();

        await BuildSut().DispatchWarningAsync(transaction.Id);

        var published = Assert.IsType<TimeoutWarningEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(14, published.RemainingMinutes);
    }

    private WarningDispatcher BuildSut() =>
        new(Context, _outbox, _clock, NullLogger<WarningDispatcher>.Instance);

    private Transaction NewEscrowedTransaction(DateTime paymentDeadline)
    {
        var transaction = TimeoutTestFixtures.NewTransaction(
            _seller.Id, TransactionStatus.ITEM_ESCROWED,
            _clock.GetUtcNow().UtcDateTime,
            paymentDeadline: paymentDeadline,
            buyerId: _buyer.Id,
            buyerRefundAddress: TimeoutTestFixtures.ValidWallet);
        transaction.EscrowBotAssetId = "100200300-bot";
        return transaction;
    }

    private sealed class RecordingOutboxService : IOutboxService
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
