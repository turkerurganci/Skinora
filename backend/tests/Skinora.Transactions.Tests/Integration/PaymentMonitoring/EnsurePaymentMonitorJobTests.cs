using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.PaymentMonitoring;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Transactions.Tests.Integration.PaymentAddresses;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.PaymentMonitoring;

/// <summary>
/// T139 — the reconciler that arms, re-arms and disarms the sidecar's active
/// payment monitors. These tests are the evidence for the defect the task
/// closes: before T139 nothing ever called the sidecar, and a happy-path
/// PaymentAddress row never left <c>MonitoringStatus.ACTIVE</c>.
/// </summary>
public class EnsurePaymentMonitorJobTests : IntegrationTestBase
{
    static EnsurePaymentMonitorJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string Wallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";
    private const string HotWallet = "THotWalletFixtureAddrFakeFakeFake1";

    private User _seller = null!;
    private User _buyer = null!;
    private StubBlockchainSidecarClient _sidecar = null!;
    private int _nextIndex;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000401",
            SteamDisplayName = "Seller",
            DefaultPayoutAddress = Wallet,
            MobileAuthenticatorVerified = true,
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000402",
            SteamDisplayName = "Buyer",
            DefaultRefundAddress = Wallet,
            MobileAuthenticatorVerified = true,
        };
        context.Set<User>().AddRange(_seller, _buyer);
        await context.SaveChangesAsync();

        _sidecar = new StubBlockchainSidecarClient();
        _nextIndex = 5000;
    }

    // ─── Arm ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TransactionStatus.SELLER_CONFIRMED)]
    [InlineData(TransactionStatus.PAYMENT_RECEIVED)]
    [InlineData(TransactionStatus.ITEM_DELIVERED)]
    public async Task Open_Window_Is_Armed_And_The_Row_Stays_Active(TransactionStatus status)
    {
        var (tx, address) = await SeedAddressAsync(status, MonitoringStatus.ACTIVE);

        await BuildSut().ExecuteAsync(CancellationToken.None);

        var call = Assert.Single(_sidecar.MonitorStartCalls);
        Assert.Equal(address.Address, call.Address);
        Assert.Equal(address.Id, call.PaymentAddressId);
        Assert.Equal(tx.Id, call.TransactionId);
        Assert.Equal("USDT", call.ExpectedSymbol);
        Assert.Equal("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", call.ExpectedContract);
        Assert.Empty(_sidecar.MonitorStopCalls);

        Assert.Equal(MonitoringStatus.ACTIVE, await ReadStatusAsync(address.Id));
    }

    [Fact]
    public async Task Arming_Is_Repeated_Every_Run_So_A_Sidecar_Restart_Self_Heals()
    {
        // The sidecar registry is in-memory and no backend hook observes a
        // sidecar restart. Re-issuing start is the recovery mechanism, and it
        // is safe because MonitorRegistry.start is idempotent per address.
        await SeedAddressAsync(TransactionStatus.SELLER_CONFIRMED, MonitoringStatus.ACTIVE);

        await BuildSut().ExecuteAsync(CancellationToken.None);
        await BuildSut().ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, _sidecar.MonitorStartCalls.Count);
    }

    [Fact]
    public async Task A_Failed_Arm_Leaves_The_Row_Alone_For_The_Next_Run()
    {
        var (_, address) = await SeedAddressAsync(
            TransactionStatus.SELLER_CONFIRMED, MonitoringStatus.ACTIVE);
        _sidecar.MonitorStartResponses.Enqueue(BlockchainSidecarStatus.Unavailable);

        await BuildSut().ExecuteAsync(CancellationToken.None);

        Assert.Equal(MonitoringStatus.ACTIVE, await ReadStatusAsync(address.Id));
    }

    // ─── Disarm ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TransactionStatus.COMPLETED)]
    [InlineData(TransactionStatus.REFUNDED)]
    [InlineData(TransactionStatus.CANCELLED_ADMIN)]
    public async Task Terminal_Status_Stops_The_Monitor_And_Stamps_Stopped(
        TransactionStatus status)
    {
        // This is the leak T139 closes: MonitoringStatus.ACTIVE had exactly one
        // writer (the allocator) and one exit (the cancel pipeline), so a
        // completed transaction kept its address inside ReconciliationService's
        // scan scope forever.
        var (_, address) = await SeedAddressAsync(status, MonitoringStatus.ACTIVE);

        await BuildSut().ExecuteAsync(CancellationToken.None);

        Assert.Equal(address.Address, Assert.Single(_sidecar.MonitorStopCalls));
        Assert.Empty(_sidecar.MonitorStartCalls);
        Assert.Equal(MonitoringStatus.STOPPED, await ReadStatusAsync(address.Id));
    }

    [Fact]
    public async Task A_Swept_Deposit_Is_Disarmed_While_The_Transaction_Is_Still_Live()
    {
        var (_, address) = await SeedAddressAsync(
            TransactionStatus.ITEM_DELIVERED, MonitoringStatus.ACTIVE);
        await SeedSweepAsync(address, BlockchainTransactionStatus.CONFIRMED);

        await BuildSut().ExecuteAsync(CancellationToken.None);

        Assert.Single(_sidecar.MonitorStopCalls);
        Assert.Equal(MonitoringStatus.STOPPED, await ReadStatusAsync(address.Id));
    }

    [Fact]
    public async Task An_Unconfirmed_Sweep_Does_Not_Close_The_Window()
    {
        // The deposit still holds the money until the sweep confirms on-chain,
        // and a refund can still be drawn from it (05 §3.3 pre-sweep refund).
        var (_, address) = await SeedAddressAsync(
            TransactionStatus.ITEM_DELIVERED, MonitoringStatus.ACTIVE);
        await SeedSweepAsync(address, BlockchainTransactionStatus.PENDING);

        await BuildSut().ExecuteAsync(CancellationToken.None);

        Assert.Single(_sidecar.MonitorStartCalls);
        Assert.Empty(_sidecar.MonitorStopCalls);
        Assert.Equal(MonitoringStatus.ACTIVE, await ReadStatusAsync(address.Id));
    }

    [Fact]
    public async Task An_Unacknowledged_Stop_Does_Not_Stamp_Stopped()
    {
        // Stamping STOPPED on a sidecar that never heard the stop would hide a
        // still-running monitor from every query that could find it again.
        var (_, address) = await SeedAddressAsync(
            TransactionStatus.COMPLETED, MonitoringStatus.ACTIVE);
        _sidecar.MonitorStopResponses.Enqueue(BlockchainSidecarStatus.Unavailable);

        await BuildSut().ExecuteAsync(CancellationToken.None);

        Assert.Equal(MonitoringStatus.ACTIVE, await ReadStatusAsync(address.Id));
    }

    // ─── Out of scope ───────────────────────────────────────────────────

    [Theory]
    [InlineData(TransactionStatus.CREATED)]
    [InlineData(TransactionStatus.ACCEPTED)]
    [InlineData(TransactionStatus.FLAGGED)]
    public async Task An_Unopened_Window_Is_Not_Touched(TransactionStatus status)
    {
        var (_, address) = await SeedAddressAsync(status, MonitoringStatus.ACTIVE);

        await BuildSut().ExecuteAsync(CancellationToken.None);

        Assert.Empty(_sidecar.MonitorStartCalls);
        Assert.Empty(_sidecar.MonitorStopCalls);
        Assert.Equal(MonitoringStatus.ACTIVE, await ReadStatusAsync(address.Id));
    }

    [Theory]
    [InlineData(MonitoringStatus.POST_CANCEL_24H)]
    [InlineData(MonitoringStatus.STOPPED)]
    public async Task Rows_Outside_The_Active_Lifecycle_Are_Not_Touched(
        MonitoringStatus monitoringStatus)
    {
        // POST_CANCEL_* belongs to the T75 registry and STOPPED is already done.
        var (_, address) = await SeedAddressAsync(TransactionStatus.CANCELLED_TIMEOUT, monitoringStatus);

        await BuildSut().ExecuteAsync(CancellationToken.None);

        Assert.Empty(_sidecar.MonitorStartCalls);
        Assert.Empty(_sidecar.MonitorStopCalls);
        Assert.Equal(monitoringStatus, await ReadStatusAsync(address.Id));
    }

    [Fact]
    public async Task Soft_Deleted_Addresses_Are_Ignored()
    {
        var (_, address) = await SeedAddressAsync(
            TransactionStatus.SELLER_CONFIRMED, MonitoringStatus.ACTIVE, softDeleted: true);

        await BuildSut().ExecuteAsync(CancellationToken.None);

        Assert.Empty(_sidecar.MonitorStartCalls);
        Assert.Empty(_sidecar.MonitorStopCalls);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private EnsurePaymentMonitorJob BuildSut()
        => new(Context, _sidecar, NullLogger<EnsurePaymentMonitorJob>.Instance);

    private async Task<MonitoringStatus> ReadStatusAsync(Guid paymentAddressId)
        => (await Context.Set<PaymentAddress>()
                .AsNoTracking()
                .IgnoreQueryFilters()
                .SingleAsync(p => p.Id == paymentAddressId))
            .MonitoringStatus;

    private async Task SeedSweepAsync(PaymentAddress address, BlockchainTransactionStatus status)
    {
        // 06 §3.8 — CK_BlockchainTransactions_Status_* tie ConfirmationCount and
        // ConfirmedAt to Status, and CK_..._Type_Sweep requires the deposit id.
        var nowUtc = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        Context.Set<BlockchainTransaction>().Add(new BlockchainTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = address.TransactionId,
            PaymentAddressId = address.Id,
            Type = BlockchainTransactionType.SWEEP,
            Token = StablecoinType.USDT,
            Amount = 102m,
            Status = status,
            FromAddress = address.Address,
            ToAddress = HotWallet,
            TxHash = $"tx-{Guid.NewGuid():N}",
            ConfirmationCount = status == BlockchainTransactionStatus.CONFIRMED ? 20 : 5,
            ConfirmedAt = status == BlockchainTransactionStatus.CONFIRMED
                ? nowUtc
                : (DateTime?)null,
            CreatedAt = nowUtc,
        });
        await Context.SaveChangesAsync();
    }

    private async Task<(Transaction Transaction, PaymentAddress Address)> SeedAddressAsync(
        TransactionStatus status,
        MonitoringStatus monitoringStatus,
        bool softDeleted = false)
    {
        var nowUtc = new DateTime(2026, 8, 20, 11, 0, 0, DateTimeKind.Utc);
        // CK_Transactions_Cancel — REFUNDED reuses the cancellation fields
        // (WP5 buyer-favor resolution), so it needs the same attribution as the
        // CANCELLED_* rows.
        var needsCancelAttribution = status is TransactionStatus.CANCELLED_TIMEOUT
            or TransactionStatus.CANCELLED_SELLER
            or TransactionStatus.CANCELLED_BUYER
            or TransactionStatus.CANCELLED_ADMIN
            or TransactionStatus.REFUNDED;

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = _buyer.SteamId,
            Status = status,
            CancelledBy = needsCancelAttribution ? CancelledByType.TIMEOUT : null,
            CancelReason = needsCancelAttribution ? "Fixture cancellation" : null,
            CancelledAt = needsCancelAttribution ? nowUtc : null,
            BuyerRefundAddress = Wallet,
            ItemAssetId = $"asset-{_nextIndex}",
            ItemClassId = "abc-class",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = Wallet,
            PaymentTimeoutMinutes = 1440,
            AcceptedAt = nowUtc.AddMinutes(-30),
            SellerReadyConfirmedAt = nowUtc.AddMinutes(-25),
        };
        Context.Set<Transaction>().Add(transaction);

        var address = new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            Address = $"TDepositFixtureAddr{_nextIndex:D14}",
            HdWalletIndex = _nextIndex++,
            ExpectedAmount = transaction.TotalAmount,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = monitoringStatus,
            MonitoringExpiresAt = monitoringStatus == MonitoringStatus.ACTIVE
                ? (DateTime?)null
                : nowUtc.AddDays(1),
            IsDeleted = softDeleted,
            DeletedAt = softDeleted ? nowUtc : null,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
        Context.Set<PaymentAddress>().Add(address);
        await Context.SaveChangesAsync();

        return (transaction, address);
    }
}
