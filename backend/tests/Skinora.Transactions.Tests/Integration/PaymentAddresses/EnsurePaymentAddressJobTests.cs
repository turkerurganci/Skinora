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
    public async Task Picks_Up_Accepted_Transactions_Without_PaymentAddress()
    {
        // T147 — ACCEPTED and SELLER_CONFIRMED: the network is chosen at
        // acceptance, and the confirmed state is the one that shows the address.
        var t1 = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var t2 = await SeedTransactionAsync(TransactionStatus.SELLER_CONFIRMED);

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
        var t = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
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
    public async Task Ignores_FLAGGED_And_CREATED_Listings_No_Network_Yet()
    {
        await SeedTransactionAsync(TransactionStatus.FLAGGED);
        await SeedTransactionAsync(TransactionStatus.CREATED);
        var accepted = await SeedTransactionAsync(TransactionStatus.ACCEPTED);

        var sut = BuildJob();
        await sut.ExecuteAsync();

        // Only one sidecar call — for the ACCEPTED transaction. FLAGGED and
        // CREATED have no network yet (T147), so there is nothing to derive on.
        var call = Assert.Single(_sidecar.Calls);
        Assert.Equal(accepted.Id, call.TransactionId);
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
        var t1 = await SeedTransactionAsync(TransactionStatus.ACCEPTED);
        var t2 = await SeedTransactionAsync(TransactionStatus.ACCEPTED);

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
            await SeedTransactionAsync(TransactionStatus.ACCEPTED);
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
            await SeedTransactionAsync(TransactionStatus.ACCEPTED, BlockchainNetwork.ETHEREUM);
        }

        // Newest of all: one TRON transaction, and one accepted row without a
        // network — T147 (11 §F8 kararı xiii): no default network is invented
        // for it, so it is not a candidate either.
        var tron = await SeedTransactionAsync(TransactionStatus.ACCEPTED, BlockchainNetwork.TRON);
        var networkless = await SeedTransactionAsync(TransactionStatus.ACCEPTED, withoutNetwork: true);

        var registry = FakeBlockchainServiceRegistry.ForTron(sidecar: _sidecar);
        await BuildJob(registry).ExecuteAsync();

        var call = Assert.Single(_sidecar.Calls);
        Assert.Equal(tron.Id, call.TransactionId);
        Assert.DoesNotContain(registry.Requests, r => r.Network == BlockchainNetwork.ETHEREUM);
        await using var verify = CreateContext();
        Assert.True(await verify.Set<PaymentAddress>().AnyAsync(p => p.TransactionId == tron.Id));
        Assert.False(await verify.Set<PaymentAddress>().AnyAsync(p => p.TransactionId == networkless.Id));
        Assert.Equal(1, await verify.Set<PaymentAddress>().CountAsync());
    }

    [Fact]
    public async Task Only_Transactions_On_A_Configured_Network_Are_Candidates()
    {
        // With only Ethereum configured, the TRON transaction waits and the
        // Ethereum one is derived on Ethereum's service.
        var tron = await SeedTransactionAsync(TransactionStatus.ACCEPTED, BlockchainNetwork.TRON);
        var eth = await SeedTransactionAsync(TransactionStatus.ACCEPTED, BlockchainNetwork.ETHEREUM);

        var registry = new FakeBlockchainServiceRegistry().With(BlockchainNetwork.ETHEREUM, sidecar: _sidecar);
        await BuildJob(registry).ExecuteAsync();

        var call = Assert.Single(_sidecar.Calls);
        Assert.Equal(eth.Id, call.TransactionId);
        Assert.DoesNotContain(registry.Requests, r => r.Network == BlockchainNetwork.TRON);
        await using var verify = CreateContext();
        Assert.True(await verify.Set<PaymentAddress>().AnyAsync(p => p.TransactionId == eth.Id));
        Assert.False(await verify.Set<PaymentAddress>().AnyAsync(p => p.TransactionId == tron.Id));
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
        BlockchainNetwork? network = null,
        bool withoutNetwork = false)
    {
        var nowUtc = DateTime.UtcNow;
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
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
            // T147 — the network is chosen at acceptance; a listing has none.
            Network = withoutNetwork || status is TransactionStatus.CREATED or TransactionStatus.FLAGGED
                ? null
                : network ?? BlockchainNetwork.TRON,
            PaymentTimeoutMinutes = 24 * 60,
            AcceptDeadline = status == TransactionStatus.CREATED ? nowUtc.AddHours(1) : null,
        };
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx;
    }
}
