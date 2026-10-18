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
/// Integration coverage for <see cref="EnsurePaymentAddressJob"/> (T70). The
/// recurring sweep is the safety net that recovers any transaction whose
/// inline <see cref="PaymentAddressAllocator"/> call lost the sidecar
/// round-trip (transient outage). Tests exercise the eligibility filter,
/// batch boundary, and partial-failure tolerance.
/// </summary>
public class EnsurePaymentAddressJobTests : IntegrationTestBase
{
    static EnsurePaymentAddressJobTests()
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
            SteamId = "76561198000000500",
            SteamDisplayName = "EnsureJob Seller",
            WalletAddresses = { UserWalletAddress.For(BlockchainNetwork.TRON, WalletAddressType.PAYOUT, SellerWallet) },
            MobileAuthenticatorVerified = true,
        };
        context.Set<User>().Add(_seller);
        await context.SaveChangesAsync();
        _sidecar = new StubBlockchainSidecarClient();
    }

    [Fact]
    public async Task Picks_Up_CREATED_Transactions_Without_PaymentAddress()
    {
        var t1 = await SeedTransactionAsync(TransactionStatus.CREATED);
        var t2 = await SeedTransactionAsync(TransactionStatus.CREATED);

        var sut = BuildJob();
        await sut.ExecuteAsync();

        await using var verify = CreateContext();
        Assert.True(await verify.Set<PaymentAddress>()
            .AnyAsync(p => p.TransactionId == t1.Id));
        Assert.True(await verify.Set<PaymentAddress>()
            .AnyAsync(p => p.TransactionId == t2.Id));
    }

    [Fact]
    public async Task Skips_Transactions_That_Already_Have_PaymentAddress()
    {
        var t = await SeedTransactionAsync(TransactionStatus.CREATED);
        Context.Set<PaymentAddress>().Add(new PaymentAddress
        {
            Id = Guid.NewGuid(),
            TransactionId = t.Id,
            Address = "TPreExisting00000000000000000000000",
            HdWalletIndex = 0,
            ExpectedAmount = 100.00m,
            ExpectedToken = StablecoinType.USDT,
            MonitoringStatus = MonitoringStatus.ACTIVE,
        });
        await Context.SaveChangesAsync();

        var sut = BuildJob();
        await sut.ExecuteAsync();

        // No sidecar call because the eligibility filter excluded the row at
        // the top-level SELECT (PaymentAddress was non-null).
        Assert.Empty(_sidecar.Calls);
    }

    [Fact]
    public async Task Ignores_FLAGGED_Transactions()
    {
        await SeedTransactionAsync(TransactionStatus.FLAGGED);
        await SeedTransactionAsync(TransactionStatus.CREATED);

        var sut = BuildJob();
        await sut.ExecuteAsync();

        // Only one sidecar call — for the CREATED transaction; FLAGGED was
        // filtered out because it is not in EligibleStates.
        Assert.Single(_sidecar.Calls);
    }

    [Fact]
    public async Task Ignores_Terminal_State_Transactions()
    {
        // COMPLETED is the canonical terminal state for this assertion;
        // CANCELLED_* requires CK_Transactions_Cancel side-fields that the
        // bare-bones seed helper does not populate, so we keep this test
        // focused on a single representative non-eligible status.
        await SeedTransactionAsync(TransactionStatus.COMPLETED);

        var sut = BuildJob();
        await sut.ExecuteAsync();

        Assert.Empty(_sidecar.Calls);
    }

    [Fact]
    public async Task Continues_On_Per_Transaction_Failure()
    {
        var t1 = await SeedTransactionAsync(TransactionStatus.CREATED);
        var t2 = await SeedTransactionAsync(TransactionStatus.CREATED);

        // First sidecar call fails (NotConfigured), second succeeds. We can't
        // guarantee the ordering of t1/t2 since the job uses CreatedAt order
        // and both rows share the same instant — but ExactlyOne should land.
        _sidecar.Responses.Enqueue(new BlockchainSidecarDeriveResult(
            BlockchainSidecarStatus.Unavailable, null, null));
        // Second call (and onward) falls back to the default Success response.

        var sut = BuildJob();
        await sut.ExecuteAsync();

        // Exactly one row was allocated; the other was skipped on the failure.
        await using var verify = CreateContext();
        var allocated = await verify.Set<PaymentAddress>()
            .Where(p => p.TransactionId == t1.Id || p.TransactionId == t2.Id)
            .CountAsync();
        Assert.Equal(1, allocated);
        Assert.Equal(2, _sidecar.Calls.Count);
    }

    [Fact]
    public async Task Respects_BatchSize_Per_Run()
    {
        // Seed BatchSize + 2 eligible transactions and verify the job
        // processes at most BatchSize per execution.
        for (var i = 0; i < EnsurePaymentAddressJob.BatchSize + 2; i++)
        {
            await SeedTransactionAsync(TransactionStatus.CREATED);
        }

        var sut = BuildJob();
        await sut.ExecuteAsync();

        Assert.Equal(EnsurePaymentAddressJob.BatchSize, _sidecar.Calls.Count);
    }

    // T148 düzeltme turu 1 (F1) — derivation on a network without a service
    // fails closed every run and the transaction stays address-less. Picked
    // oldest-first it would hold the whole batch forever; the TRON transaction
    // behind BatchSize (and more) of them must get its address on the first run.
    [Theory]
    [InlineData(EnsurePaymentAddressJob.BatchSize)]
    [InlineData(EnsurePaymentAddressJob.BatchSize + 5)]
    public async Task Transactions_On_An_Unconfigured_Network_Do_Not_Starve_Tron(int parked)
    {
        // CreatedAt is stamped by AppDbContext on each save, so seeding one by
        // one makes the Ethereum transactions strictly the oldest.
        for (var i = 0; i < parked; i++)
        {
            await SeedTransactionAsync(TransactionStatus.CREATED, BlockchainNetwork.ETHEREUM);
        }

        // Newest of all; one explicitly TRON, one legacy (no network = TRON).
        var tron = await SeedTransactionAsync(TransactionStatus.CREATED, BlockchainNetwork.TRON);
        var legacy = await SeedTransactionAsync(TransactionStatus.CREATED, network: null);

        var registry = FakeBlockchainServiceRegistry.ForTron(sidecar: _sidecar);
        await BuildJob(registry).ExecuteAsync();

        Assert.Equal(2, _sidecar.Calls.Count);
        Assert.DoesNotContain(registry.Requests, r => r.Network == BlockchainNetwork.ETHEREUM);
        await using var verify = CreateContext();
        Assert.True(await verify.Set<PaymentAddress>().AnyAsync(p => p.TransactionId == tron.Id));
        Assert.True(await verify.Set<PaymentAddress>().AnyAsync(p => p.TransactionId == legacy.Id));
        Assert.Equal(2, await verify.Set<PaymentAddress>().CountAsync());
    }

    [Fact]
    public async Task Legacy_Transactions_Wait_When_Tron_Itself_Is_Unconfigured()
    {
        // No network = TRON (06 §1.4): with only Ethereum configured, the
        // legacy transaction is not a candidate, the Ethereum one is.
        var legacy = await SeedTransactionAsync(TransactionStatus.CREATED, network: null);
        var eth = await SeedTransactionAsync(TransactionStatus.CREATED, BlockchainNetwork.ETHEREUM);

        var registry = new FakeBlockchainServiceRegistry().With(BlockchainNetwork.ETHEREUM, sidecar: _sidecar);
        await BuildJob(registry).ExecuteAsync();

        Assert.Single(_sidecar.Calls);
        await using var verify = CreateContext();
        Assert.True(await verify.Set<PaymentAddress>().AnyAsync(p => p.TransactionId == eth.Id));
        Assert.False(await verify.Set<PaymentAddress>().AnyAsync(p => p.TransactionId == legacy.Id));
    }

    private EnsurePaymentAddressJob BuildJob(FakeBlockchainServiceRegistry? registry = null)
    {
        registry ??= FakeBlockchainServiceRegistry.ForTron(sidecar: _sidecar);
        var allocator = new PaymentAddressAllocator(
            Context, registry, NullLogger<PaymentAddressAllocator>.Instance);
        return new EnsurePaymentAddressJob(
            Context, allocator, registry, NullLogger<EnsurePaymentAddressJob>.Instance);
    }

    private async Task<Transaction> SeedTransactionAsync(
        TransactionStatus status,
        BlockchainNetwork? network = null)
    {
        var nowUtc = DateTime.UtcNow;
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Network = network,
            Status = status,
            SellerId = _seller.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000501",
            ItemAssetId = "AssetId" + Guid.NewGuid().ToString("N")[..10],
            ItemClassId = "ClassId",
            ItemInstanceId = "InstanceId",
            ItemName = "AK-47 | Test",
            StablecoinType = StablecoinType.USDT,
            Price = 98.00m,
            CommissionRate = 0.02m,
            CommissionAmount = 2.00m,
            TotalAmount = 100.00m,
            SellerPayoutAddress = SellerWallet,
            PaymentTimeoutMinutes = 24 * 60,
            AcceptDeadline = status == TransactionStatus.CREATED ? nowUtc.AddHours(1) : null,
        };
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx;
    }
}
