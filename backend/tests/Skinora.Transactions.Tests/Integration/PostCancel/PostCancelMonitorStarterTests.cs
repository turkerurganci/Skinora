using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.PostCancel;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.PostCancel;

/// <summary>
/// T75 — exercise <see cref="PostCancelMonitorStarter"/> covering the three
/// idempotency branches and the happy-path stamp+outbox publish.
/// </summary>
public class PostCancelMonitorStarterTests : IntegrationTestBase
{
    static PostCancelMonitorStarterTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string Wallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";
    private const string DepositAddress = "TPostCancelDepositAddrFakeFakeFakeXX";

    private User _seller = null!;
    private User _buyer = null!;
    private RecordingOutboxService _outbox = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000301",
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = Wallet,
            MobileAuthenticatorVerified = true,
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000302",
            SteamDisplayName = "Buyer",
            DefaultRefundAddress = Wallet,
            MobileAuthenticatorVerified = true,
        };
        context.Set<User>().AddRange(_seller, _buyer);
        await context.SaveChangesAsync();
        _outbox = new RecordingOutboxService();
    }

    [Fact]
    public async Task RequestStart_NoPaymentAddress_IsNoOp_NothingPublished()
    {
        var sut = BuildSut();
        var fakeTransactionId = Guid.NewGuid();

        await sut.RequestStartAsync(fakeTransactionId, DateTime.UtcNow, CancellationToken.None);

        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task RequestStart_ActiveMonitoring_StampsPostCancel24H_AndPublishesOutboxEvent()
    {
        var tx = await CreateTransactionWithPaymentAddressAsync(MonitoringStatus.ACTIVE);
        var cancelledAt = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);

        var sut = BuildSut();
        await sut.RequestStartAsync(tx.Id, cancelledAt, CancellationToken.None);
        await Context.SaveChangesAsync();

        var persisted = await Context.Set<PaymentAddress>()
            .AsNoTracking()
            .SingleAsync(p => p.TransactionId == tx.Id);
        Assert.Equal(MonitoringStatus.POST_CANCEL_24H, persisted.MonitoringStatus);
        Assert.Equal(cancelledAt.AddHours(24), persisted.MonitoringExpiresAt);

        var published = Assert.Single(_outbox.Published);
        var evt = Assert.IsType<PostCancelMonitorStartRequestedEvent>(published);
        Assert.Equal(tx.Id, evt.TransactionId);
        Assert.Equal(persisted.Id, evt.PaymentAddressId);
        Assert.Equal(DepositAddress, evt.Address);
        Assert.Equal(StablecoinType.USDT, evt.ExpectedToken);
        Assert.Equal("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", evt.ExpectedContractAddress);
        Assert.Equal(cancelledAt, evt.CancelledAt);
    }

    [Theory]
    [InlineData(MonitoringStatus.POST_CANCEL_24H)]
    [InlineData(MonitoringStatus.POST_CANCEL_7D)]
    [InlineData(MonitoringStatus.POST_CANCEL_30D)]
    [InlineData(MonitoringStatus.STOPPED)]
    public async Task RequestStart_AlreadyInPostCancelLifecycle_IsNoOp(MonitoringStatus existing)
    {
        var tx = await CreateTransactionWithPaymentAddressAsync(existing);
        var beforeExpiresAt = (await Context.Set<PaymentAddress>().AsNoTracking()
            .SingleAsync(p => p.TransactionId == tx.Id)).MonitoringExpiresAt;
        var cancelledAt = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);

        var sut = BuildSut();
        await sut.RequestStartAsync(tx.Id, cancelledAt, CancellationToken.None);
        await Context.SaveChangesAsync();

        var persisted = await Context.Set<PaymentAddress>()
            .AsNoTracking()
            .SingleAsync(p => p.TransactionId == tx.Id);
        Assert.Equal(existing, persisted.MonitoringStatus);
        Assert.Equal(beforeExpiresAt, persisted.MonitoringExpiresAt);
        Assert.Empty(_outbox.Published);
    }

    [Fact]
    public async Task RequestStart_UsdcAddress_PublishesCorrectContract()
    {
        var tx = await CreateTransactionWithPaymentAddressAsync(MonitoringStatus.ACTIVE, StablecoinType.USDC);
        var cancelledAt = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);

        var sut = BuildSut();
        await sut.RequestStartAsync(tx.Id, cancelledAt, CancellationToken.None);

        var evt = Assert.IsType<PostCancelMonitorStartRequestedEvent>(Assert.Single(_outbox.Published));
        Assert.Equal(StablecoinType.USDC, evt.ExpectedToken);
        Assert.Equal("TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8", evt.ExpectedContractAddress);
    }

    private PostCancelMonitorStarter BuildSut()
        => new(Context, _outbox, NullLogger<PostCancelMonitorStarter>.Instance);

    private sealed class RecordingOutboxService : IOutboxService
    {
        public List<IDomainEvent> Published { get; } = new();

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private async Task<Transaction> CreateTransactionWithPaymentAddressAsync(
        MonitoringStatus monitoringStatus,
        StablecoinType token = StablecoinType.USDT)
    {
        var nowUtc = new DateTime(2026, 5, 17, 11, 0, 0, DateTimeKind.Utc);
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            Status = TransactionStatus.CANCELLED_TIMEOUT,
            CancelledBy = CancelledByType.TIMEOUT,
            CancelReason = "Test cancellation",
            CancelledAt = nowUtc,
            BuyerRefundAddress = Wallet,
            ItemAssetId = "asset-1",
            ItemClassId = "abc-class",
            ItemName = "AK-47 | Redline",
            StablecoinType = token,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = Wallet,
            PaymentTimeoutMinutes = 1440,
            AcceptedAt = nowUtc.AddMinutes(-30),
            ItemEscrowedAt = nowUtc.AddMinutes(-25),
            EscrowBotAssetId = "200300400",
        };
        Context.Set<Transaction>().Add(transaction);

        var paymentAddress = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            Address = DepositAddress,
            HdWalletIndex = (int)(DateTime.UtcNow.Ticks % int.MaxValue),
            ExpectedAmount = transaction.TotalAmount,
            ExpectedToken = token,
            MonitoringStatus = monitoringStatus,
            MonitoringExpiresAt = monitoringStatus == MonitoringStatus.ACTIVE
                ? (DateTime?)null
                : nowUtc.AddDays(1),
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        Context.Set<PaymentAddress>().Add(paymentAddress);
        await Context.SaveChangesAsync();

        return transaction;
    }
}
