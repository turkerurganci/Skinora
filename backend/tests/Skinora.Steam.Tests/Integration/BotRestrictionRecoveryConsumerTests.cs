using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Steam.Application.Recovery;
using Skinora.Steam.Domain.Entities;
using Skinora.Steam.Infrastructure.Persistence;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Steam.Tests.Integration;

/// <summary>
/// T103b-2 — BotRestrictionRecoveryConsumer integration tests against a real
/// SQL Server. Verifies the stuck-escrow predicate, eager materialisation of
/// recovery items, auto-EMERGENCY_HOLD (composed with the real
/// <see cref="TimeoutFreezeService"/>), idempotency, and the terminal / already-held
/// edge cases.
/// </summary>
public sealed class BotRestrictionRecoveryConsumerTests : IntegrationTestBase
{
    static BotRestrictionRecoveryConsumerTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        SteamModuleDbRegistration.RegisterSteamModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private User _seller = null!;
    private User _buyer = null!;
    private PlatformSteamBot _bot = null!;
    private PlatformSteamBot _otherBot = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000000401", SteamDisplayName = "Seller" };
        _buyer = new User { Id = Guid.NewGuid(), SteamId = "76561198000000402", SteamDisplayName = "Buyer" };
        context.Set<User>().AddRange(_seller, _buyer);

        _bot = new PlatformSteamBot
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198099999401",
            DisplayName = "EscrowBot-Recovery",
            Status = PlatformSteamBotStatus.RESTRICTED,
            RestrictionReason = "restricted",
        };
        _otherBot = new PlatformSteamBot
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198099999402",
            DisplayName = "EscrowBot-Healthy",
            Status = PlatformSteamBotStatus.ACTIVE,
        };
        context.Set<PlatformSteamBot>().AddRange(_bot, _otherBot);

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Restriction_MaterialisesAndHolds_StuckEscrows()
    {
        // Two stuck escrows on the restricted bot (one ITEM_ESCROWED, one
        // PAYMENT_RECEIVED — the latter exercises the freeze pre-pass for a
        // non-ITEM_ESCROWED state) plus one delivered (not stuck).
        var escrowedId = await AddTransactionAsync(TransactionStatus.ITEM_ESCROWED, _bot.Id, assetOnBot: "asset-1");
        var paidId = await AddTransactionAsync(TransactionStatus.PAYMENT_RECEIVED, _bot.Id, assetOnBot: "asset-2");
        var deliveredId = await AddTransactionAsync(
            TransactionStatus.COMPLETED, _bot.Id, assetOnBot: "asset-3", deliveredAssetId: "buyer-asset-3");

        var sut = CreateSut(out var outbox);
        await sut.Handle(RestrictedEvent(), CancellationToken.None);

        await using var verify = CreateContext();
        var items = await verify.Set<BotRecoveryItem>().Where(r => r.PlatformSteamBotId == _bot.Id).ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(BotRecoveryStatus.PENDING, i.RecoveryStatus));
        Assert.DoesNotContain(items, i => i.TransactionId == deliveredId);

        var escrowItem = Assert.Single(items, i => i.TransactionId == escrowedId);
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, escrowItem.StatusAtRestriction);

        // Both stuck transactions are auto-held; the delivered one is untouched.
        var escrowTx = await verify.Set<Transaction>().SingleAsync(t => t.Id == escrowedId);
        var paidTx = await verify.Set<Transaction>().SingleAsync(t => t.Id == paidId);
        var deliveredTx = await verify.Set<Transaction>().SingleAsync(t => t.Id == deliveredId);
        Assert.True(escrowTx.IsOnHold);
        Assert.True(paidTx.IsOnHold);
        Assert.Equal(TimeoutFreezeReason.EMERGENCY_HOLD, paidTx.TimeoutFreezeReason);
        Assert.NotNull(paidTx.TimeoutRemainingSeconds);
        Assert.False(deliveredTx.IsOnHold);

        // One EMERGENCY_HOLD_APPLIED event per auto-held transaction.
        var holds = outbox.Events.OfType<EmergencyHoldAppliedEvent>().ToList();
        Assert.Equal(2, holds.Count);

        // Audit: 2 recovery-item-created + 2 emergency-hold rows.
        Assert.Equal(2, await verify.Set<AuditLog>()
            .CountAsync(a => a.Action == AuditAction.BOT_RECOVERY_ITEM_CREATED));
        Assert.Equal(2, await verify.Set<AuditLog>()
            .CountAsync(a => a.Action == AuditAction.EMERGENCY_HOLD_APPLIED));
    }

    [Fact]
    public async Task Restriction_SkipsNonStuck_NoAsset_OtherBot()
    {
        // EscrowBotId set but asset never captured → item never reached the bot.
        await AddTransactionAsync(TransactionStatus.TRADE_OFFER_SENT_TO_SELLER, _bot.Id, assetOnBot: null);
        // In custody but on a different (healthy) bot.
        await AddTransactionAsync(TransactionStatus.ITEM_ESCROWED, _otherBot.Id, assetOnBot: "asset-other");

        var sut = CreateSut(out _);
        await sut.Handle(RestrictedEvent(), CancellationToken.None);

        await using var verify = CreateContext();
        Assert.Empty(await verify.Set<BotRecoveryItem>().ToListAsync());
    }

    [Fact]
    public async Task Restriction_TerminalStuck_MaterialisesWithoutHold()
    {
        // Cancelled but the refund offer never completed → item still in the bot.
        var cancelledId = await AddTransactionAsync(
            TransactionStatus.CANCELLED_ADMIN, _bot.Id, assetOnBot: "asset-c", cancelled: true);

        var sut = CreateSut(out var outbox);
        await sut.Handle(RestrictedEvent(), CancellationToken.None);

        await using var verify = CreateContext();
        var item = Assert.Single(await verify.Set<BotRecoveryItem>().ToListAsync());
        Assert.Equal(cancelledId, item.TransactionId);
        Assert.Equal(TransactionStatus.CANCELLED_ADMIN, item.StatusAtRestriction);

        // Terminal → no hold attempted, no hold event.
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == cancelledId);
        Assert.False(tx.IsOnHold);
        Assert.Empty(outbox.Events.OfType<EmergencyHoldAppliedEvent>());
    }

    [Fact]
    public async Task Restriction_RefundAccepted_IsSkipped()
    {
        var refundedId = await AddTransactionAsync(
            TransactionStatus.CANCELLED_ADMIN, _bot.Id, assetOnBot: "asset-r", cancelled: true);
        await using (var arrange = CreateContext())
        {
            arrange.Set<TradeOffer>().Add(new TradeOffer
            {
                Id = Guid.NewGuid(),
                TransactionId = refundedId,
                PlatformSteamBotId = _bot.Id,
                Direction = TradeOfferDirection.RETURN_TO_SELLER,
                SteamTradeOfferId = "refund-1",
                Status = TradeOfferStatus.ACCEPTED,
                SentAt = DateTime.UtcNow,
                RespondedAt = DateTime.UtcNow,
            });
            await arrange.SaveChangesAsync();
        }

        var sut = CreateSut(out _);
        await sut.Handle(RestrictedEvent(), CancellationToken.None);

        await using var verify = CreateContext();
        Assert.Empty(await verify.Set<BotRecoveryItem>().ToListAsync());
    }

    [Fact]
    public async Task Restriction_AlreadyHeld_MaterialisesButDoesNotRehold()
    {
        var heldId = await AddTransactionAsync(
            TransactionStatus.ITEM_ESCROWED, _bot.Id, assetOnBot: "asset-h", alreadyHeld: true);

        var sut = CreateSut(out var outbox);
        await sut.Handle(RestrictedEvent(), CancellationToken.None);

        await using var verify = CreateContext();
        Assert.Single(await verify.Set<BotRecoveryItem>().ToListAsync());
        // Still held (untouched), and no new hold event was emitted.
        var tx = await verify.Set<Transaction>().SingleAsync(t => t.Id == heldId);
        Assert.True(tx.IsOnHold);
        Assert.Empty(outbox.Events.OfType<EmergencyHoldAppliedEvent>());
    }

    [Fact]
    public async Task Restriction_IsIdempotent_SecondRunCreatesNothing()
    {
        await AddTransactionAsync(TransactionStatus.ITEM_ESCROWED, _bot.Id, assetOnBot: "asset-1");

        await CreateSut(out _).Handle(RestrictedEvent(), CancellationToken.None);
        // Second delivery of the same restriction event.
        await CreateSut(out var outbox2).Handle(RestrictedEvent(), CancellationToken.None);

        await using var verify = CreateContext();
        Assert.Single(await verify.Set<BotRecoveryItem>().ToListAsync());
        // No second hold (already held on first run → skipped).
        Assert.Empty(outbox2.Events.OfType<EmergencyHoldAppliedEvent>());
    }

    [Fact]
    public async Task Restriction_NoStuckItems_IsNoOp()
    {
        var sut = CreateSut(out var outbox);
        await sut.Handle(RestrictedEvent(), CancellationToken.None);

        await using var verify = CreateContext();
        Assert.Empty(await verify.Set<BotRecoveryItem>().ToListAsync());
        Assert.Empty(outbox.Events);
    }

    // ---------- helpers ----------

    private BotRestrictedEvent RestrictedEvent() => new(
        EventId: Guid.NewGuid(),
        PlatformSteamBotId: _bot.Id,
        SteamId: _bot.SteamId,
        DisplayName: _bot.DisplayName,
        Status: PlatformSteamBotStatus.RESTRICTED.ToString(),
        Reason: "restricted",
        OccurredAt: DateTime.UtcNow);

    private BotRestrictionRecoveryConsumer CreateSut(out RecordingOutbox outbox)
    {
        outbox = new RecordingOutbox();
        var freeze = new TimeoutFreezeService(
            Context, new NoOpJobScheduler(), new NoOpScheduling(), TimeProvider.System);
        var materialiser = new BotRecoveryMaterialiser(
            Context,
            freeze,
            outbox,
            new AuditLogger(Context, TimeProvider.System),
            TimeProvider.System);
        return new BotRestrictionRecoveryConsumer(
            Context,
            materialiser,
            NullLogger<BotRestrictionRecoveryConsumer>.Instance);
    }

    private async Task<Guid> AddTransactionAsync(
        TransactionStatus status,
        Guid escrowBotId,
        string? assetOnBot,
        string? deliveredAssetId = null,
        bool cancelled = false,
        bool alreadyHeld = false)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var arrange = CreateContext();
        arrange.Set<Transaction>().Add(new Transaction
        {
            Id = id,
            Status = status,
            SellerId = _seller.Id,
            BuyerId = _buyer.Id,
            EscrowBotId = escrowBotId,
            EscrowBotAssetId = assetOnBot,
            DeliveredBuyerAssetId = deliveredAssetId,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = _buyer.SteamId,
            BuyerRefundAddress = "TKnEzG4qX5n6ZRSeller7B9C2D3E4F5G6H7",
            ItemAssetId = "asset-src",
            ItemClassId = "cls",
            ItemName = "AK-47 | Redline",
            ItemIconUrl = "https://cdn.test/ak.png",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.03m,
            CommissionAmount = 3m,
            TotalAmount = 103m,
            SellerPayoutAddress = "TKnEzG4qX5n6ZRBuyer7B9C2D3E4F5G6H7",
            CancelledBy = cancelled ? CancelledByType.ADMIN : null,
            CancelReason = cancelled ? "admin cancel" : null,
            CancelledAt = cancelled ? now : null,
            IsOnHold = alreadyHeld,
            EmergencyHoldAt = alreadyHeld ? now : null,
            EmergencyHoldReason = alreadyHeld ? "pre-existing hold" : null,
            EmergencyHoldByAdminId = alreadyHeld ? _seller.Id : null,
            TimeoutFrozenAt = alreadyHeld ? now : null,
            TimeoutFreezeReason = alreadyHeld ? TimeoutFreezeReason.EMERGENCY_HOLD : null,
            TimeoutRemainingSeconds = alreadyHeld ? 0 : null,
        });
        await arrange.SaveChangesAsync();
        return id;
    }

    private sealed class RecordingOutbox : IOutboxService
    {
        public List<IDomainEvent> Events { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpJobScheduler : IBackgroundJobScheduler
    {
        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay) => Guid.NewGuid().ToString("N");
        public string Enqueue<T>(Expression<Action<T>> methodCall) => Guid.NewGuid().ToString("N");
        public bool Delete(string jobId) => true;
        public void AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>> methodCall, string cronExpression) { }
    }

    private sealed class NoOpScheduling : ITimeoutSchedulingService
    {
        public Task<TimeoutJobIds> SchedulePaymentTimeoutAsync(Guid transactionId, CancellationToken cancellationToken)
            => Task.FromResult(new TimeoutJobIds("p", "w"));

        public Task CancelTimeoutJobsAsync(Guid transactionId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<TimeoutJobIds> ReschedulePaymentTimeoutAsync(
            Guid transactionId, TimeSpan remaining, DateTime newPaymentDeadlineUtc, CancellationToken cancellationToken)
            => Task.FromResult(new TimeoutJobIds("p", "w"));
    }
}
