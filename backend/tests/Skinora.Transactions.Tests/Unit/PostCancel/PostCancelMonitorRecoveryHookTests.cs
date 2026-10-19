using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.PostCancel;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Transactions.Tests.Helpers;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Unit.PostCancel;

/// <summary>
/// T158 (h) doğrulaması — the startup replay of post-cancel monitors
/// (<see cref="PostCancelMonitorRecoveryHook"/>) is the one path that re-arms a
/// cancelled Ethereum deposit after a backend restart. Without the persisted
/// start block the service would fall back to T151's look-back rule, and a
/// payment older than that window — exactly the late payment the post-cancel
/// monitor exists for — would be missed. SQLite in-memory, so the persisted
/// column is read through the real EF mapping.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PostCancelMonitorRecoveryHookTests : IDisposable
{
    private const string DepositAddress = "0x5b38da6a701c568545dcfcb03fcb875f56beddc4";

    static PostCancelMonitorRecoveryHookTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public PostCancelMonitorRecoveryHookTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Theory]
    [InlineData(26_214_908L)]
    [InlineData(null)]
    public async Task T158h_The_Persisted_Start_Block_Reaches_The_Replayed_PostCancel_Start(long? monitorFromBlock)
    {
        // The null row is a pre-T158 / TRON deposit: the block stays null, so
        // the HTTP client leaves the field out of the body (TRON body unchanged).
        await SeedPostCancelAddressAsync(monitorFromBlock);
        var sidecar = new RecordingPostCancelSidecarClient();
        var registry = new FakeBlockchainServiceRegistry().With(BlockchainNetwork.ETHEREUM, sidecar: sidecar);

        await BuildSut(registry).StartAsync(CancellationToken.None);

        var request = Assert.Single(sidecar.PostCancelRequests);
        Assert.Equal(DepositAddress, request.Address);
        Assert.Equal(monitorFromBlock, request.WatchFromBlock);
        Assert.Equal(new[] { ("Sidecar", BlockchainNetwork.ETHEREUM) }, registry.Requests);
    }

    private PostCancelMonitorRecoveryHook BuildSut(IBlockchainServiceRegistry registry)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(_options));
        services.AddSingleton(registry);
        services.AddSingleton<IStablecoinContractRegistry>(
            StablecoinContractRegistry.FromOptions(new StablecoinContractOptions()));
        var provider = services.BuildServiceProvider();
        return new PostCancelMonitorRecoveryHook(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PostCancelMonitorRecoveryHook>.Instance);
    }

    private async Task SeedPostCancelAddressAsync(long? monitorFromBlock)
    {
        var cancelledAt = new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc);
        await using var db = new AppDbContext(_options);
        var seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000001581",
            SteamDisplayName = "Seller",
            CreatedAt = cancelledAt,
        };
        var buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000001582",
            SteamDisplayName = "Buyer",
            CreatedAt = cancelledAt,
        };
        db.Set<User>().AddRange(seller, buyer);

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = TransactionStatus.CANCELLED_ADMIN,
            SellerId = seller.Id,
            BuyerId = buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = buyer.SteamId,
            BuyerRefundAddress = "0x8ba1f109551bd432803012645ac136ddd64dba72",
            ItemAssetId = "asset-1",
            ItemClassId = "cls",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = "0x71c7656ec7ab88b098defb751b7401b5f6d8976f",
            Network = BlockchainNetwork.ETHEREUM,
            CancelledBy = CancelledByType.ADMIN,
            CancelReason = "admin cancel (test)",
            CancelledAt = cancelledAt,
        };
        db.Set<Transaction>().Add(transaction);

        db.Set<PaymentAddress>().Add(new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            Network = BlockchainNetwork.ETHEREUM,
            Address = DepositAddress,
            HdWalletIndex = 41,
            ExpectedAmount = transaction.TotalAmount,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.POST_CANCEL_7D,
            MonitoringExpiresAt = cancelledAt + TimeSpan.FromDays(7),
            MonitorFromBlock = monitorFromBlock,
            CreatedAt = cancelledAt,
        });
        await db.SaveChangesAsync();
    }

    private sealed class RecordingPostCancelSidecarClient : IBlockchainSidecarClient
    {
        public List<PostCancelMonitorStartRequest> PostCancelRequests { get; } = new();

        public Task<BlockchainSidecarStatus> StartPostCancelMonitoringAsync(
            PostCancelMonitorStartRequest request, CancellationToken cancellationToken)
        {
            PostCancelRequests.Add(request);
            return Task.FromResult(BlockchainSidecarStatus.Success);
        }

        public Task<BlockchainSidecarStatus> StartMonitoringAsync(
            PaymentMonitorStartRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarStatus> StopMonitoringAsync(
            string address, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarDeriveResult> DeriveAddressAsync(
            int index, Guid transactionId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarStatus> StopPostCancelMonitoringAsync(
            string address, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarBalancesResult> GetWalletBalancesAsync(
            IReadOnlyList<string> addresses, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<BlockchainSidecarTransferResult> SendHotToColdTransferAsync(
            HotToColdTransferRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
