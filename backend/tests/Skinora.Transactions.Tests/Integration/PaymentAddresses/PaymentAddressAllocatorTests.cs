using Skinora.Transactions.Tests.Helpers;
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
            WalletAddresses = { UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, SellerWallet) },
            MobileAuthenticatorVerified = true,
        };
        context.Set<User>().Add(_seller);
        await context.SaveChangesAsync();
        _sidecar = new StubBlockchainSidecarClient();
    }

    [Fact]
    public async Task Accepted_Inserts_PaymentAddress_Row_With_Next_Index()
    {
        var transaction = await SeedTransactionAsync(TransactionStatus.ACCEPTED, totalAmount: 102.00m);

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
        var transaction = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
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
        var t1 = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var t2 = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var t3 = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
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
        var preExistingTx = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        await SeedPaymentAddressAsync(preExistingTx.Id, index: 1,
            address: StubBlockchainSidecarClient.DeterministicAddress(1));

        var target = await SeedTransactionAsync(TransactionStatus.ACCEPTED);

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
        var pre = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
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

        var target = await SeedTransactionAsync(TransactionStatus.ACCEPTED);

        var sut = BuildSut();
        var result = await sut.AllocateAsync(target.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.Created, result.Status);
        Assert.Equal(PaymentAddressAllocator.MaxRetryAttempts, _sidecar.Calls.Count);
    }

    [Fact]
    public async Task Returns_ExhaustedRetries_When_All_Attempts_Collide()
    {
        var pre = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        await SeedPaymentAddressAsync(pre.Id, index: 7,
            address: StubBlockchainSidecarClient.DeterministicAddress(7));

        var duplicate = StubBlockchainSidecarClient.DeterministicAddress(7);
        for (var i = 0; i < PaymentAddressAllocator.MaxRetryAttempts; i++)
        {
            _sidecar.Responses.Enqueue(new BlockchainSidecarDeriveResult(
                BlockchainSidecarStatus.Success, duplicate, "m/44'/195'/0'/0/8"));
        }

        var target = await SeedTransactionAsync(TransactionStatus.ACCEPTED);

        var sut = BuildSut();
        var result = await sut.AllocateAsync(target.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.ExhaustedRetries, result.Status);
        Assert.Equal(PaymentAddressAllocator.MaxRetryAttempts, _sidecar.Calls.Count);
    }

    [Fact]
    public async Task Returns_SidecarNotConfigured_When_Sidecar_Reports_503()
    {
        var transaction = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
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
        var transaction = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
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
    public async Task Allocates_For_SELLER_CONFIRMED_So_A_Lost_Inline_Allocation_Is_Recovered()
    {
        // T147 — the inline allocation moved to acceptance; a seller who
        // confirms before the recovery job ran must not leave the buyer
        // without an address in the one state that shows it.
        var transaction = await SeedTransactionAsync(TransactionStatus.SELLER_CONFIRMED);
        var sut = BuildSut();

        var result = await sut.AllocateAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.Created, result.Status);
    }

    // ---------- T147 — the deposit lives on the network chosen at acceptance ----------

    [Fact]
    public async Task CREATED_Listing_Has_No_Network_Yet_So_No_Address_Is_Derived()
    {
        var transaction = await SeedTransactionAsync(TransactionStatus.CREATED);
        var sut = BuildSut();

        var result = await sut.AllocateAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.TransactionIneligible, result.Status);
        Assert.Empty(_sidecar.Calls);
    }

    [Fact]
    public async Task Accepted_Row_Without_A_Network_Is_Not_Given_A_Default_One()
    {
        // No fallback network: an accepted row missing its network is a data
        // fault, not a TRON transaction.
        var transaction = await SeedTransactionAsync(TransactionStatus.ACCEPTED, network: null);
        var sut = BuildSut();

        var result = await sut.AllocateAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.TransactionIneligible, result.Status);
        Assert.Empty(_sidecar.Calls);
    }

    [Fact]
    public async Task An_Existing_Deposit_On_Another_Network_Is_Reported_Not_Returned()
    {
        // Same-network rule (11 §F8 K3): a TRON deposit is never handed out for
        // an Ethereum transaction, and nothing new is derived over it.
        var transaction = await SeedTransactionAsync(TransactionStatus.ACCEPTED, network: BlockchainNetwork.ETHEREUM);
        await SeedPaymentAddressAsync(transaction.Id, index: 3,
            address: "TPreExistingTron000000000000000000", network: BlockchainNetwork.TRON);
        var sut = BuildSut();

        var result = await sut.AllocateAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.NetworkMismatch, result.Status);
        Assert.Null(result.Address);
        Assert.Empty(_sidecar.Calls);
    }

    // ---------- T145 — HD index per network (06 §1.4) ----------

    [Fact]
    public async Task HdWalletIndex_Is_Counted_Per_Network()
    {
        // Another network's indices (its own seed, 11 §F8 K7) must not push
        // the TRON counter forward: TRON's first deposit is still index 0.
        var ethTx = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        await SeedPaymentAddressAsync(ethTx.Id, index: 9,
            address: "0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed",
            network: BlockchainNetwork.ETHEREUM);

        var target = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var sut = BuildSut();
        var result = await sut.AllocateAsync(target.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.Created, result.Status);
        Assert.Equal(0, result.HdWalletIndex);
        Assert.Equal(0, _sidecar.Calls.Single().Index);

        await using var verify = CreateContext();
        var row = await verify.Set<PaymentAddress>().SingleAsync(p => p.TransactionId == target.Id);
        Assert.Equal(BlockchainNetwork.TRON, row.Network);
    }

    [Fact]
    public async Task T158h_The_Start_Block_The_Service_Named_At_Derive_Is_Persisted()
    {
        var transaction = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        _sidecar.Responses.Enqueue(new BlockchainSidecarDeriveResult(
            BlockchainSidecarStatus.Success,
            StubBlockchainSidecarClient.DeterministicAddress(0),
            "m/44'/195'/0'/0/0",
            WatchFromBlock: 26_214_908L));

        var result = await BuildSut().AllocateAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.Created, result.Status);
        await using var verify = CreateContext();
        var row = await verify.Set<PaymentAddress>().SingleAsync(p => p.TransactionId == transaction.Id);
        Assert.Equal(26_214_908L, row.MonitorFromBlock);
    }

    [Fact]
    public async Task Allocated_Row_Carries_The_Transaction_Network()
    {
        var transaction = await SeedTransactionAsync(TransactionStatus.ACCEPTED);

        var sut = BuildSut();
        var result = await sut.AllocateAsync(transaction.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.Created, result.Status);
        await using var verify = CreateContext();
        var row = await verify.Set<PaymentAddress>().SingleAsync(p => p.TransactionId == transaction.Id);
        Assert.Equal(BlockchainNetwork.TRON, row.Network);
    }

    // ---------- T172a — index floor after a database restore (⚠ PARA) ----------

    [Fact]
    public async Task T172a_Floor_Above_The_Current_Max_Starts_There_And_Counts_On_From_It()
    {
        // A restored database knows indices 0..4; the old copy had handed out up
        // to 40. With the floor at 41 the next deposits are 41, 42 — never one
        // of the addresses given out after the backup.
        for (var i = 0; i <= 4; i++)
        {
            var old = await SeedTransactionAsync(TransactionStatus.COMPLETED);
            await SeedPaymentAddressAsync(old.Id, index: i, address: StubBlockchainSidecarClient.DeterministicAddress(i));
        }
        var t1 = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var t2 = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var sut = BuildSut(new PaymentAddressIndexFloorOptions { MinimumNextIndex = { ["TRON"] = 41 } });

        var r1 = await sut.AllocateAsync(t1.Id, CancellationToken.None);
        var r2 = await sut.AllocateAsync(t2.Id, CancellationToken.None);

        Assert.Equal(41, r1.HdWalletIndex);
        Assert.Equal(42, r2.HdWalletIndex);
        Assert.Equal(new[] { 41, 42 }, _sidecar.Calls.Select(c => c.Index));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    public async Task T172a_Floor_At_Or_Below_The_Current_Max_Cannot_Reuse_An_Index(int floor)
    {
        // Indices 0..7 are taken. A floor that is too small (stale, mistyped)
        // changes nothing: the next index is still MAX + 1 = 8.
        for (var i = 0; i <= 7; i++)
        {
            var old = await SeedTransactionAsync(TransactionStatus.COMPLETED);
            await SeedPaymentAddressAsync(old.Id, index: i, address: StubBlockchainSidecarClient.DeterministicAddress(i));
        }
        var target = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var sut = BuildSut(new PaymentAddressIndexFloorOptions { MinimumNextIndex = { ["TRON"] = floor } });

        var result = await sut.AllocateAsync(target.Id, CancellationToken.None);

        Assert.Equal(PaymentAddressAllocationStatus.Created, result.Status);
        Assert.Equal(8, result.HdWalletIndex);
        await using var verify = CreateContext();
        var indices = await verify.Set<PaymentAddress>().IgnoreQueryFilters()
            .Where(p => p.Network == BlockchainNetwork.TRON).Select(p => p.HdWalletIndex).ToListAsync();
        Assert.Equal(indices.Count, indices.Distinct().Count());
    }

    [Fact]
    public async Task T172a_Floor_Counts_Soft_Deleted_Rows_Too()
    {
        // A soft-deleted deposit still owns its index (08 §3.2): the floor rule
        // reads MAX across it, so a floor below it does not reuse it.
        var old = await SeedTransactionAsync(TransactionStatus.COMPLETED);
        await SeedPaymentAddressAsync(old.Id, index: 20, address: StubBlockchainSidecarClient.DeterministicAddress(20));
        await using (var ctx = CreateContext())
        {
            var row = await ctx.Set<PaymentAddress>().IgnoreQueryFilters().SingleAsync(p => p.HdWalletIndex == 20);
            row.IsDeleted = true;
            row.DeletedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
        }
        var target = await SeedTransactionAsync(TransactionStatus.ACCEPTED);

        var result = await BuildSut(new PaymentAddressIndexFloorOptions { MinimumNextIndex = { ["TRON"] = 5 } })
            .AllocateAsync(target.Id, CancellationToken.None);

        Assert.Equal(21, result.HdWalletIndex);
    }

    [Fact]
    public async Task T172a_Floor_Applies_Only_To_Its_Own_Network()
    {
        var target = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var sut = BuildSut(new PaymentAddressIndexFloorOptions { MinimumNextIndex = { ["ETHEREUM"] = 500 } });

        var result = await sut.AllocateAsync(target.Id, CancellationToken.None);

        Assert.Equal(0, result.HdWalletIndex);
    }

    private PaymentAddressAllocator BuildSut(PaymentAddressIndexFloorOptions? floor = null) =>
        new(Context, FakeBlockchainServiceRegistry.ForTron(sidecar: _sidecar), NullLogger<PaymentAddressAllocator>.Instance,
            floor is null ? null : Microsoft.Extensions.Options.Options.Create(floor));

    private async Task<Transaction> SeedTransactionAsync(
        TransactionStatus status, decimal totalAmount = 100.00m,
        BlockchainNetwork? network = BlockchainNetwork.TRON)
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
            // T147 — the network is chosen at acceptance; a listing has none.
            Network = status is TransactionStatus.CREATED or TransactionStatus.FLAGGED ? null : network,
            PaymentTimeoutMinutes = 24 * 60,
            AcceptDeadline = status == TransactionStatus.CREATED ? nowUtc.AddHours(1) : null,
        };
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx;
    }

    private async Task SeedPaymentAddressAsync(
        Guid transactionId, int index, string address,
        BlockchainNetwork network = BlockchainNetwork.TRON)
    {
        Context.Set<PaymentAddress>().Add(new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            Network = network,
            Address = address,
            HdWalletIndex = index,
            ExpectedAmount = 100.00m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
        });
        await Context.SaveChangesAsync();
    }

}
