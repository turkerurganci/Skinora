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
    public async Task Recompute_Refunded_By_Delivery_Reversal_Counts_Against_Seller_Only()
    {
        // T129 — 06 §3.1. A trade the seller pulled back after being paid is the
        // heaviest fraud in the model; before this arm existed it left the
        // reputation score untouched and lived only in a fraud flag an admin had
        // to go and read.
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -100);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.REFUNDED, dayOffset: -60,
            deliveryReversedAt: DateTime.UtcNow.AddDays(-60));

        var aggregator = new ReputationAggregator(Context);

        var aliceSnap = await aggregator.RecomputeAsync(_alice.Id, CancellationToken.None);
        var bobSnap = await aggregator.RecomputeAsync(_bob.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        // Alice (seller): 1 success out of 2 counted.
        Assert.Equal(1, aliceSnap.CompletedTransactionCount);
        Assert.Equal(0.5m, aliceSnap.SuccessfulTransactionRate);

        // Bob (buyer) is the victim here — the reversal must not touch his rate.
        Assert.Equal(1, bobSnap.CompletedTransactionCount);
        Assert.Equal(1m, bobSnap.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task Recompute_Refunded_By_Admin_Dispute_Excludes_Both_Parties()
    {
        // The other producer of REFUNDED (WP5 buyer-favour ruling) stays out of
        // the formula for the same reason CANCELLED_ADMIN does: it is a platform
        // decision, not a proven user fault (02 §13). The discriminator is
        // DeliveryReversedAt, which only the settlement path sets.
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -100);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.REFUNDED, dayOffset: -60);

        var aggregator = new ReputationAggregator(Context);

        var aliceSnap = await aggregator.RecomputeAsync(_alice.Id, CancellationToken.None);
        var bobSnap = await aggregator.RecomputeAsync(_bob.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(1m, aliceSnap.SuccessfulTransactionRate);
        Assert.Equal(1m, bobSnap.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task Recompute_Cancelled_Timeout_Payment_Phase_Hits_Buyer()
    {
        // PreviousStatus = SELLER_CONFIRMED → ödeme timeout'u (Adım 4) → BUYER.
        // Alice (seller) keeps her clean rate; Bob (buyer) takes the hit.
        var tx = await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.CANCELLED_TIMEOUT, dayOffset: -50);
        await InsertTimeoutHistoryAsync(tx.Id, previousStatus: TransactionStatus.SELLER_CONFIRMED);
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
    public async Task Recompute_Cancelled_Timeout_SellerConfirm_Phase_Hits_Seller()
    {
        // PreviousStatus = ACCEPTED → satıcı hazırlık onayı timeout'u (Adım 3) → SELLER.
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
    public async Task Recompute_Cancelled_Timeout_Accept_Phase_Hits_Buyer()
    {
        // PreviousStatus = CREATED → alıcı kabul timeout'u (Adım 2) → BUYER.
        var tx = await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.CANCELLED_TIMEOUT, dayOffset: -50);
        await InsertTimeoutHistoryAsync(tx.Id, previousStatus: TransactionStatus.CREATED);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -10);

        var aggregator = new ReputationAggregator(Context);
        var aliceSnap = await aggregator.RecomputeAsync(_alice.Id, CancellationToken.None);
        var bobSnap = await aggregator.RecomputeAsync(_bob.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        // Alice (seller): the buyer never accepted — not her fault.
        Assert.Equal(1m, aliceSnap.SuccessfulTransactionRate);
        // Bob (buyer): 1 success / 2 attempts = 0.5.
        Assert.Equal(0.5m, bobSnap.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task Recompute_Cancelled_Timeout_Delivery_Phase_Hits_Seller()
    {
        // PreviousStatus = PAYMENT_RECEIVED → teslimat timeout'u (Adım 6–7) → SELLER.
        //
        // v3.0 sorumluluk çevirmesi (02 §3.1, 06 §3.1): custodial modelde bu
        // pencere alıcınındı (platformun gönderdiği teslim offer'ını kabul
        // edecek taraf oydu). P2P'de trade'i satıcı gönderir — teslim etmeyen
        // satıcının itibarına yazılır ve 02 §13'e göre bu, satıcı skorunun en
        // belirleyici negatif girdisidir. Bu test o çevirmenin bekçisidir:
        // haritada PAYMENT_RECEIVED yeniden alıcıya kayarsa kırılır.
        var tx = await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.CANCELLED_TIMEOUT, dayOffset: -50);
        await InsertTimeoutHistoryAsync(tx.Id, previousStatus: TransactionStatus.PAYMENT_RECEIVED);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -10);

        var aggregator = new ReputationAggregator(Context);
        var aliceSnap = await aggregator.RecomputeAsync(_alice.Id, CancellationToken.None);
        var bobSnap = await aggregator.RecomputeAsync(_bob.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        // Alice (seller): 1 success / 2 attempts = 0.5 — the non-delivery is hers.
        Assert.Equal(0.5m, aliceSnap.SuccessfulTransactionRate);
        // Bob (buyer): paid on time and got nothing — his rate stays clean.
        Assert.Equal(1m, bobSnap.SuccessfulTransactionRate);
    }

    [Fact]
    public async Task Recompute_Cancelled_Timeout_Without_History_Row_Affects_Neither_Party()
    {
        // PreviousStatus is the ONLY input the responsibility map reads
        // (06 §3.1). If a timeout path ever flips the status without recording
        // the history row, the cancellation is silently dropped from BOTH
        // denominators — no one is penalised. Pinned so that silent drop shows
        // up as an intentional, documented behaviour rather than a surprise.
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.CANCELLED_TIMEOUT, dayOffset: -50);
        await InsertTransactionAsync(_alice.Id, _bob.Id, TransactionStatus.COMPLETED, dayOffset: -10);

        var aggregator = new ReputationAggregator(Context);
        var aliceSnap = await aggregator.RecomputeAsync(_alice.Id, CancellationToken.None);
        var bobSnap = await aggregator.RecomputeAsync(_bob.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(1m, aliceSnap.SuccessfulTransactionRate);
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
        int dayOffset,
        DateTime? deliveryReversedAt = null)
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
            ItemAssetId = Guid.NewGuid().ToString("N")[..12],
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
                                  or TransactionStatus.REFUNDED
                          ? nowUtc.AddDays(dayOffset)
                          : null,
            // CK_Transactions_Cancel: any CANCELLED_* status — and REFUNDED,
            // which reuses the same columns — requires CancelledBy +
            // CancelReason + CancelledAt all NOT NULL.
            CancelledBy = status switch
            {
                TransactionStatus.CANCELLED_SELLER => CancelledByType.SELLER,
                TransactionStatus.CANCELLED_BUYER => CancelledByType.BUYER,
                TransactionStatus.CANCELLED_TIMEOUT => CancelledByType.TIMEOUT,
                TransactionStatus.CANCELLED_ADMIN => CancelledByType.ADMIN,
                // T129 — a settlement reversal is attributed to the seller; an
                // admin dispute refund to the admin.
                TransactionStatus.REFUNDED => deliveryReversedAt is null
                    ? CancelledByType.ADMIN
                    : CancelledByType.SELLER,
                _ => null,
            },
            CancelReason = status is TransactionStatus.CANCELLED_SELLER
                                   or TransactionStatus.CANCELLED_BUYER
                                   or TransactionStatus.CANCELLED_TIMEOUT
                                   or TransactionStatus.CANCELLED_ADMIN
                                   or TransactionStatus.REFUNDED
                           ? "test"
                           : null,
            DeliveryReversedAt = deliveryReversedAt,
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
