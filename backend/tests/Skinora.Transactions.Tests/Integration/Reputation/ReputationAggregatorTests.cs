using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Reputation;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Reputation;

/// <summary>
/// End-to-end coverage for <see cref="ReputationAggregator"/> against a real
/// SQL Server instance. Tests focus on the responsibility map (06 §3.1) and
/// the wash-trading filter (02 §14.1) interaction with the rate denominator.
/// </summary>
public class ReputationAggregatorTests : IntegrationTestBase
{
    static ReputationAggregatorTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    private User _alice = null!;
    private User _bob = null!;
    private User _carol = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _alice = new User { Id = Guid.NewGuid(), SteamId = "76561198000000010", SteamDisplayName = "Alice" };
        _bob = new User { Id = Guid.NewGuid(), SteamId = "76561198000000011", SteamDisplayName = "Bob" };
        _carol = new User { Id = Guid.NewGuid(), SteamId = "76561198000000012", SteamDisplayName = "Carol" };
        context.Set<User>().AddRange(_alice, _bob, _carol);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Recompute_With_No_Transactions_Sets_Zero_Count_And_Null_Rate()
    {
        var aggregator = new ReputationAggregator(Context);

        var snapshot = await aggregator.RecomputeAsync(_alice.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(0, snapshot.CompletedTransactionCount);
        Assert.Null(snapshot.SuccessfulTransactionRate);

        var alice = await Context.Set<User>().FindAsync(_alice.Id);
        Assert.Equal(0, alice!.CompletedTransactionCount);
        Assert.Null(alice.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task Recompute_All_Completed_Yields_Rate_One()
    {
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -100);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -60);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -20);

        var snapshot = await new ReputationAggregator(Context).RecomputeAsync(_alice.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(3, snapshot.CompletedTransactionCount);
        Assert.Equal(1m, snapshot.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task Recompute_Cancelled_Seller_Counts_Against_Seller_Only()
    {
        // Alice as seller, Bob as buyer. CANCELLED_SELLER hits Alice's denom.
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -100);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.CANCELLED_SELLER, dayOffset: -60);

        var aggregator = new ReputationAggregator(Context);

        var aliceSnap = await aggregator.RecomputeAsync(_alice.Id, CancellationToken.None);
        var bobSnap = await aggregator.RecomputeAsync(_bob.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(1, aliceSnap.CompletedTransactionCount);
        Assert.Equal(0.5m, aliceSnap.SuccessfulTransactionRate);

        // Bob was a party to 1 COMPLETED only — CANCELLED_SELLER does NOT
        // count against him (the seller is responsible).
        Assert.Equal(1, bobSnap.CompletedTransactionCount);
        Assert.Equal(1m, bobSnap.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task Recompute_Cancelled_Admin_Excludes_Both_Parties()
    {
        // CANCELLED_ADMIN must not appear in either party's denominator (02 §13).
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -100);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.CANCELLED_ADMIN, dayOffset: -60);

        var aliceSnap = await new ReputationAggregator(Context).RecomputeAsync(_alice.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(1, aliceSnap.CompletedTransactionCount);
        Assert.Equal(1m, aliceSnap.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task Recompute_Cancelled_Timeout_Maps_To_Responsible_Party()
    {
        // PreviousStatus = ITEM_ESCROWED → payment timeout (Adım 4) → BUYER.
        // Alice (seller) keeps her clean rate; Bob (buyer) takes the hit.
        var tx = await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.CANCELLED_TIMEOUT, dayOffset: -50);
        await InsertTimeoutHistoryAsync(tx.Id, previousStatus: TransactionStatus.ITEM_ESCROWED);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -10);

        var aggregator = new ReputationAggregator(Context);
        var aliceSnap = await aggregator.RecomputeAsync(_alice.Id, CancellationToken.None);
        var bobSnap = await aggregator.RecomputeAsync(_bob.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        // Alice: only the COMPLETED counts (timeout is not her fault).
        Assert.Equal(1, aliceSnap.CompletedTransactionCount);
        Assert.Equal(1m, aliceSnap.SuccessfulTransactionRate);

        // Bob: 1 success / 2 attempts = 0.5.
        Assert.Equal(1, bobSnap.CompletedTransactionCount);
        Assert.Equal(0.5m, bobSnap.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task Recompute_Cancelled_Timeout_Step3_Hits_Seller()
    {
        // PreviousStatus = ACCEPTED → satıcı trade-offer timeout (Adım 3) → SELLER.
        var tx = await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.CANCELLED_TIMEOUT, dayOffset: -50);
        await InsertTimeoutHistoryAsync(tx.Id, previousStatus: TransactionStatus.ACCEPTED);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -10);

        var aggregator = new ReputationAggregator(Context);
        var aliceSnap = await aggregator.RecomputeAsync(_alice.Id, CancellationToken.None);
        var bobSnap = await aggregator.RecomputeAsync(_bob.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        // Alice (seller): 1 success / 2 attempts = 0.5.
        Assert.Equal(0.5m, aliceSnap.SuccessfulTransactionRate);
        // Bob (buyer): COMPLETED only.
        Assert.Equal(1m, bobSnap.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task Recompute_Wash_Trading_Removes_Repeat_Pair_From_Rate_Denominator()
    {
        // Same Alice↔Bob pair: two COMPLETED inside 30 days, then a third
        // COMPLETED 60 days later. Wash filter drops the middle one from the
        // rate calculation but CompletedTransactionCount stays raw.
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -100);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -90);  // washed
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -40);

        var snapshot = await new ReputationAggregator(Context).RecomputeAsync(_alice.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        // Raw count includes all three.
        Assert.Equal(3, snapshot.CompletedTransactionCount);
        // Rate denominator is 2 (washed row excluded). All counted are SUCCESS → 1.0.
        Assert.Equal(1m, snapshot.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task Recompute_Wash_Trading_Hides_Cancelled_From_Denominator()
    {
        // Day -100: Alice/Bob COMPLETED (counted).
        // Day -90:  Alice/Bob CANCELLED_SELLER (washed → no penalty).
        // Day -40:  Alice/Bob COMPLETED (counted).
        // Without the wash filter Alice would have 2/3 = 0.6666; with it she has 2/2 = 1.0.
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -100);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.CANCELLED_SELLER, dayOffset: -90);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -40);

        var snapshot = await new ReputationAggregator(Context).RecomputeAsync(_alice.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(2, snapshot.CompletedTransactionCount);
        Assert.Equal(1m, snapshot.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task Recompute_Different_Pairs_Stay_Independent()
    {
        // Alice↔Bob and Alice↔Carol are different pairs — wash filter must not
        // collapse them.
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -100);
        await InsertTransactionAsync(_alice.Id, _carol.Id, TransactionStatus.COMPLETED, dayOffset: -95);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -50);

        var snapshot = await new ReputationAggregator(Context).RecomputeAsync(_alice.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(3, snapshot.CompletedTransactionCount);
        Assert.Equal(1m, snapshot.SuccessfulTransactionRate);
    }

    // ---- helpers ----

    private async Task<Transaction> InsertTransactionAsync(
        Guid sellerId,
        Guid buyerId,
        TransactionStatus status,
        int dayOffset)
    {
        var nowUtc = DateTime.UtcNow;
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = sellerId,
            BuyerId = buyerId,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198000000099",
            ItemAssetId = "1",
            ItemClassId = "1",
            ItemName = "Test Item",
            StablecoinType = StablecoinType.USDT,
            Price = 50m,
            CommissionRate = 0.02m,
            CommissionAmount = 1m,
            TotalAmount = 51m,
            SellerPayoutAddress = "TXxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
            PaymentTimeoutMinutes = 60,
            CompletedAt = status == TransactionStatus.COMPLETED ? nowUtc.AddDays(dayOffset) : null,
            CancelledAt = status is TransactionStatus.CANCELLED_SELLER
                                  or TransactionStatus.CANCELLED_BUYER
                                  or TransactionStatus.CANCELLED_TIMEOUT
                                  or TransactionStatus.CANCELLED_ADMIN
                          ? nowUtc.AddDays(dayOffset)
                          : null,
            // CK_Transactions_Cancel: any CANCELLED_* status requires
            // CancelledBy + CancelReason + CancelledAt all NOT NULL.
            CancelledBy = status switch
            {
                TransactionStatus.CANCELLED_SELLER => CancelledByType.SELLER,
                TransactionStatus.CANCELLED_BUYER => CancelledByType.BUYER,
                TransactionStatus.CANCELLED_TIMEOUT => CancelledByType.TIMEOUT,
                TransactionStatus.CANCELLED_ADMIN => CancelledByType.ADMIN,
                _ => null,
            },
            CancelReason = status is TransactionStatus.CANCELLED_SELLER
                                   or TransactionStatus.CANCELLED_BUYER
                                   or TransactionStatus.CANCELLED_TIMEOUT
                                   or TransactionStatus.CANCELLED_ADMIN
                           ? "test"
                           : null,
        };

        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();

        // AppDbContext.UpdateAuditFields stamps CreatedAt = UtcNow on Add. Replay
        // the desired offset with a second SaveChanges so the wash-filter
        // timestamps line up (T33 dated-seed pattern).
        tx.CreatedAt = nowUtc.AddDays(dayOffset);
        await Context.SaveChangesAsync();

        return tx;
    }

    private async Task InsertTimeoutHistoryAsync(Guid transactionId, TransactionStatus previousStatus)
    {
        var history = new TransactionHistory
        {
            TransactionId = transactionId,
            PreviousStatus = previousStatus,
            NewStatus = TransactionStatus.CANCELLED_TIMEOUT,
            Trigger = "test.timeout",
            ActorType = ActorType.SYSTEM,
            ActorId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            CreatedAt = DateTime.UtcNow,
        };

        Context.Set<TransactionHistory>().Add(history);
        await Context.SaveChangesAsync();
    }
}
