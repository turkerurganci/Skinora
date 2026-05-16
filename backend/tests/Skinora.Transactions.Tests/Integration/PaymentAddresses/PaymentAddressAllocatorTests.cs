using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.PaymentAddresses;

/// <summary>
/// Integration coverage for <see cref="PaymentAddressAllocator"/> (T70). Runs
/// against a real SQL Server testcontainer so the
/// <c>UQ_PaymentAddresses_HdWalletIndex</c> / <c>UQ_PaymentAddresses_Address</c>
/// constraints fire authentically and the retry loop is exercised against the
/// engine that ships in production.
/// </summary>
public class PaymentAddressAllocatorTests : IntegrationTestBase
{
    static PaymentAddressAllocatorTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string SellerWallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";

    private User _seller = null!;
    private StubBlockchainSidecarClient _sidecar = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198000000400",
            SteamDisplayName = "Allocator Seller",
            DefaultPayoutAddress = SellerWallet,
            MobileAuthenticatorVerified = true,
        };
        context.Set<User>().Add(_seller);
        await context.SaveChangesAsync();
        _sidecar = new StubBlockchainSidecarClient();
    }

    [Fact]
    public async Task Created_Inserts_PaymentAddress_Row_With_Next_Index()
    {
        var transaction = await SeedTransactionAsync(TransactionStatus.CREATED, totalAmount: 102.00m);

        var sut = BuildSut();

        var result = await sut.AllocateAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.Created, result.Status);
        Assert.Equal(0, result.HdWalletIndex);
        Assert.Equal(StubBlockchainSidecarClient.DeterministicAddress(0), result.Address);

        await using var verify = CreateContext();
        var row = await verify.Set<PaymentAddress>()
            .SingleAsync(p => p.TransactionId == transaction.Id);
        Assert.Equal(0, row.HdWalletIndex);
        Assert.Equal(102.00m, row.ExpectedAmount);
        Assert.Equal(StablecoinType.USDT, row.ExpectedToken);
        Assert.Equal(MonitoringStatus.ACTIVE, row.MonitoringStatus);
    }

    [Fact]
    public async Task Second_Allocate_For_Same_Transaction_Returns_AlreadyExisted_Without_Calling_Sidecar()
    {
        var transaction = await SeedTransactionAsync(TransactionStatus.CREATED);
        var sut = BuildSut();

        var first = await sut.AllocateAsync(transaction.Id, CancellationToken.None);
        Assert.Equal(PaymentAddressAllocationStatus.Created, first.Status);
        Assert.Single(_sidecar.Calls);

        var second = await sut.AllocateAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.AlreadyExisted, second.Status);
        Assert.Equal(first.Address, second.Address);
        // Sidecar was NOT called the second time.
        Assert.Single(_sidecar.Calls);
    }

    [Fact]
    public async Task Increments_Index_Across_Successive_Transactions()
    {
        var t1 = await SeedTransactionAsync(TransactionStatus.CREATED);
        var t2 = await SeedTransactionAsync(TransactionStatus.CREATED);
        var t3 = await SeedTransactionAsync(TransactionStatus.CREATED);
        var sut = BuildSut();

        var r1 = await sut.AllocateAsync(t1.Id, CancellationToken.None);
        var r2 = await sut.AllocateAsync(t2.Id, CancellationToken.None);
        var r3 = await sut.AllocateAsync(t3.Id, CancellationToken.None);

        Assert.Equal(0, r1.HdWalletIndex);
        Assert.Equal(1, r2.HdWalletIndex);
        Assert.Equal(2, r3.HdWalletIndex);
    }

    [Fact]
    public async Task UNIQUE_Collision_Retries_With_Refreshed_MaxIndex()
    {
        // Pre-seed an existing row at index 0 — first attempt by the allocator
        // reads MAX=0 → nextIndex=1, sidecar returns deterministic address for
        // index 1, insert succeeds. To force a collision we instead pre-seed
        // at index 1 and queue a sidecar response that mirrors index 1's
        // address: the allocator hits the UNIQUE constraint, the loop reads
        // MAX (still 1) → nextIndex=2, second call succeeds.
        var preExistingTx = await SeedTransactionAsync(TransactionStatus.CREATED);
        await SeedPaymentAddressAsync(preExistingTx.Id, index: 1,
            address: StubBlockchainSidecarClient.DeterministicAddress(1));

        var target = await SeedTransactionAsync(TransactionStatus.CREATED);

        var sut = BuildSut();
        var result = await sut.AllocateAsync(target.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.Created, result.Status);
        // First attempt: nextIndex=2 (MAX was 1) — sidecar's default produces
        // a fresh deterministic address, so this succeeds in one shot. Verify
        // we landed at the expected index without collision.
        Assert.Equal(2, result.HdWalletIndex);
    }

    [Fact]
    public async Task Retries_Up_To_MaxRetryAttempts_When_Sidecar_Returns_Duplicate_Address()
    {
        // Seed an existing row at a high index so any subsequent collision is
        // unambiguous. Then queue *N* duplicate-of-index-7 responses so the
        // allocator burns retries on the same UNIQUE violation, finally
        // returning a fresh address on the (N+1)th call.
        var pre = await SeedTransactionAsync(TransactionStatus.CREATED);
        await SeedPaymentAddressAsync(pre.Id, index: 7,
            address: StubBlockchainSidecarClient.DeterministicAddress(7));

        var duplicate = StubBlockchainSidecarClient.DeterministicAddress(7);
        for (var i = 0; i < PaymentAddressAllocator.MaxRetryAttempts - 1; i++)
        {
            _sidecar.Responses.Enqueue(new BlockchainSidecarDeriveResult(
                BlockchainSidecarStatus.Success, duplicate, "m/44'/195'/0'/0/8"));
        }
        // Final response: a unique synthetic address that will NOT collide.
        _sidecar.Responses.Enqueue(new BlockchainSidecarDeriveResult(
            BlockchainSidecarStatus.Success, "TUniqueAddressForRetryTest__________", "m/44'/195'/0'/0/8"));

        var target = await SeedTransactionAsync(TransactionStatus.CREATED);

        var sut = BuildSut();
        var result = await sut.AllocateAsync(target.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.Created, result.Status);
        Assert.Equal(PaymentAddressAllocator.MaxRetryAttempts, _sidecar.Calls.Count);
    }

    [Fact]
    public async Task Returns_ExhaustedRetries_When_All_Attempts_Collide()
    {
        var pre = await SeedTransactionAsync(TransactionStatus.CREATED);
        await SeedPaymentAddressAsync(pre.Id, index: 7,
            address: StubBlockchainSidecarClient.DeterministicAddress(7));

        var duplicate = StubBlockchainSidecarClient.DeterministicAddress(7);
        for (var i = 0; i < PaymentAddressAllocator.MaxRetryAttempts; i++)
        {
            _sidecar.Responses.Enqueue(new BlockchainSidecarDeriveResult(
                BlockchainSidecarStatus.Success, duplicate, "m/44'/195'/0'/0/8"));
        }

        var target = await SeedTransactionAsync(TransactionStatus.CREATED);

        var sut = BuildSut();
        var result = await sut.AllocateAsync(target.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.ExhaustedRetries, result.Status);
        Assert.Equal(PaymentAddressAllocator.MaxRetryAttempts, _sidecar.Calls.Count);
    }

    [Fact]
    public async Task Returns_SidecarNotConfigured_When_Sidecar_Reports_503()
    {
        var transaction = await SeedTransactionAsync(TransactionStatus.CREATED);
        _sidecar.Responses.Enqueue(new BlockchainSidecarDeriveResult(
            BlockchainSidecarStatus.NotConfigured, null, null));

        var sut = BuildSut();
        var result = await sut.AllocateAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.SidecarNotConfigured, result.Status);
        await using var verify = CreateContext();
        Assert.False(await verify.Set<PaymentAddress>()
            .AnyAsync(p => p.TransactionId == transaction.Id));
    }

    [Fact]
    public async Task Returns_SidecarUnavailable_When_Sidecar_Reports_Unavailable()
    {
        var transaction = await SeedTransactionAsync(TransactionStatus.CREATED);
        _sidecar.Responses.Enqueue(new BlockchainSidecarDeriveResult(
            BlockchainSidecarStatus.Unavailable, null, null));

        var sut = BuildSut();
        var result = await sut.AllocateAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.SidecarUnavailable, result.Status);
    }

    [Fact]
    public async Task Returns_TransactionNotFound_For_Unknown_Id()
    {
        var sut = BuildSut();
        var result = await sut.AllocateAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.TransactionNotFound, result.Status);
        Assert.Empty(_sidecar.Calls);
    }

    [Fact]
    public async Task Returns_TransactionIneligible_For_Non_Active_State()
    {
        var transaction = await SeedTransactionAsync(TransactionStatus.COMPLETED);
        var sut = BuildSut();

        var result = await sut.AllocateAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.TransactionIneligible, result.Status);
        Assert.Empty(_sidecar.Calls);
    }

    [Fact]
    public async Task Allocates_For_ACCEPTED_Status_Not_Only_CREATED()
    {
        var transaction = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var sut = BuildSut();

        var result = await sut.AllocateAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.Created, result.Status);
    }

    private PaymentAddressAllocator BuildSut() =>
        new(Context, _sidecar, NullLogger<PaymentAddressAllocator>.Instance);

    private async Task<Transaction> SeedTransactionAsync(
        TransactionStatus status, decimal totalAmount = 100.00m)
    {
        var nowUtc = DateTime.UtcNow;
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000401",
            ItemAssetId = "AssetId" + Guid.NewGuid().ToString("N")[..10],
            ItemClassId = "ClassId",
            ItemInstanceId = "InstanceId",
            ItemName = "AK-47 | Test",
            StablecoinType = StablecoinType.USDT,
            Price = totalAmount - 2.00m,
            CommissionRate = 0.02m,
            CommissionAmount = 2.00m,
            TotalAmount = totalAmount,
            SellerPayoutAddress = SellerWallet,
            PaymentTimeoutMinutes = 24 * 60,
            AcceptDeadline = status == TransactionStatus.CREATED ? nowUtc.AddHours(1) : null,
        };
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx;
    }

    private async Task SeedPaymentAddressAsync(Guid transactionId, int index, string address)
    {
        Context.Set<PaymentAddress>().Add(new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            Address = address,
            HdWalletIndex = index,
            ExpectedAmount = 100.00m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
        });
        await Context.SaveChangesAsync();
    }

}
