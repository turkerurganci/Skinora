using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Application.UserSuspension;
using Skinora.Shared.Domain;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Reputation;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Application.Reputation;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Reputation;

/// <summary>
/// 02 §14.2 — the non-delivery sanction end to end against SQL Server: which
/// transitions count, the window, the flag threshold, the automatic suspension
/// and the idempotency that keeps an admin's decision from being overridden.
/// </summary>
/// <remarks>
/// The event kind is a <c>[Theory]</c> axis wherever a test could pass for one
/// kind by accident: a fixture that always used delivery timeouts would pin
/// nothing about seller cancels or reversals (#321 — a fixture value that
/// coincides with the broken alternative makes the test blind to it).
/// </remarks>
public class NonDeliveryAbuseEvaluatorTests : IntegrationTestBase
{
    static NonDeliveryAbuseEvaluatorTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
    }

    public enum EventKind
    {
        DeliveryTimeout,
        SellerCancelAfterPayment,
        DeliveryReversed,
    }

    private User _seller = null!;
    private User _otherSeller = null!;
    private User _buyer = null!;
    private FakeTimeProvider _clock = null!;
    private FakeFlagPort _flags = null!;
    private CapturingAuditLogger _audit = null!;
    private CapturingOutbox _outbox = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User { Id = Guid.NewGuid(), SteamId = "76561198000000060", SteamDisplayName = "Seller" };
        _otherSeller = new User { Id = Guid.NewGuid(), SteamId = "76561198000000062", SteamDisplayName = "Other seller" };
        _buyer = new User { Id = Guid.NewGuid(), SteamId = "76561198000000061", SteamDisplayName = "Buyer" };
        context.Set<User>().AddRange(_seller, _otherSeller, _buyer);
        await context.SaveChangesAsync();

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        _flags = new FakeFlagPort();
        _audit = new CapturingAuditLogger();
        _outbox = new CapturingOutbox();
    }

    // ---- counting and thresholds ----

    [Theory]
    [InlineData(EventKind.DeliveryTimeout)]
    [InlineData(EventKind.SellerCancelAfterPayment)]
    [InlineData(EventKind.DeliveryReversed)]
    public async Task First_Event_Is_Counted_But_Not_Sanctioned(EventKind kind)
    {
        var trigger = await InsertEventAsync(_seller.Id, kind, daysAgo: 0);

        var outcome = await BuildSut().EvaluateAsync(trigger.Id, CancellationToken.None);

        Assert.Equal(NonDeliveryAbuseAction.BelowThreshold, outcome.Action);
        Assert.Equal(1, outcome.EventCount);
        Assert.Empty(_flags.Staged);
        Assert.False((await ReloadSellerAsync()).IsSuspended);
    }

    [Theory]
    [InlineData(EventKind.DeliveryTimeout)]
    [InlineData(EventKind.SellerCancelAfterPayment)]
    [InlineData(EventKind.DeliveryReversed)]
    public async Task Second_Event_Flags_The_Account_With_The_Repeat_Pattern(EventKind kind)
    {
        // The earlier event is a DIFFERENT kind, so each run proves two kinds
        // are counted together rather than one kind twice.
        await InsertEventAsync(_seller.Id, Next(kind), daysAgo: 10);
        var trigger = await InsertEventAsync(_seller.Id, kind, daysAgo: 0);

        var outcome = await BuildSut().EvaluateAsync(trigger.Id, CancellationToken.None);

        Assert.Equal(NonDeliveryAbuseAction.Flagged, outcome.Action);
        Assert.Equal(2, outcome.EventCount);
        var flag = Assert.Single(_flags.Staged);
        Assert.Equal(_seller.Id, flag.UserId);
        Assert.Equal(FraudFlagType.ABNORMAL_BEHAVIOR, flag.Type);
        using var details = JsonDocument.Parse(flag.Details);
        Assert.Equal(NonDeliveryAbuseEvaluator.FlagPattern, details.RootElement.GetProperty("pattern").GetString());
        // The admin reviews from the description — it must name both trades.
        var description = details.RootElement.GetProperty("description").GetString()!;
        Assert.Contains(trigger.Id.ToString(), description);
        Assert.False((await ReloadSellerAsync()).IsSuspended);
    }

    [Theory]
    [InlineData(EventKind.DeliveryTimeout)]
    [InlineData(EventKind.SellerCancelAfterPayment)]
    [InlineData(EventKind.DeliveryReversed)]
    public async Task Third_Event_Suspends_Until_An_Admin_Lifts_It(EventKind kind)
    {
        await InsertEventAsync(_seller.Id, Next(kind), daysAgo: 20);
        await InsertEventAsync(_seller.Id, Next(Next(kind)), daysAgo: 10);
        var trigger = await InsertEventAsync(_seller.Id, kind, daysAgo: 0);

        var outcome = await BuildSut().EvaluateAsync(trigger.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(NonDeliveryAbuseAction.Suspended, outcome.Action);
        Assert.Equal(3, outcome.EventCount);

        var seller = await ReloadSellerAsync();
        Assert.True(seller.IsSuspended);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, seller.SuspendedAt);
        // No expiry: owner decision — the suspension lasts until an admin lifts it.
        Assert.Null(seller.SuspensionExpiresAt);
        Assert.False(string.IsNullOrWhiteSpace(seller.SuspensionReason));

        var audit = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.USER_BANNED, audit.Action);
        Assert.Equal(ActorType.SYSTEM, audit.ActorType);
        Assert.Equal(SeedConstants.SystemUserId, audit.ActorId);
        var suspendedEvent = Assert.Single(_outbox.Events.OfType<AccountSuspendedEvent>());
        Assert.Equal(_seller.Id, suspendedEvent.UserId);
        Assert.Null(suspendedEvent.ExpiresAt);

        // No flag was pending, so the evidence travels with a new one.
        Assert.Single(_flags.Staged);
    }

    [Fact]
    public async Task Suspension_Does_Not_Duplicate_A_Flag_Already_Awaiting_Review()
    {
        await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 20);
        await InsertEventAsync(_seller.Id, EventKind.SellerCancelAfterPayment, daysAgo: 10);
        var trigger = await InsertEventAsync(_seller.Id, EventKind.DeliveryReversed, daysAgo: 0);
        _flags.PendingAbnormalBehavior = true;

        var outcome = await BuildSut().EvaluateAsync(trigger.Id, CancellationToken.None);

        Assert.Equal(NonDeliveryAbuseAction.Suspended, outcome.Action);
        Assert.Empty(_flags.Staged);
    }

    [Fact]
    public async Task Flag_Threshold_Does_Not_Reflag_While_One_Awaits_Review()
    {
        await InsertEventAsync(_seller.Id, EventKind.DeliveryReversed, daysAgo: 5);
        var trigger = await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 0);
        _flags.PendingAbnormalBehavior = true;

        var outcome = await BuildSut().EvaluateAsync(trigger.Id, CancellationToken.None);

        Assert.Equal(NonDeliveryAbuseAction.FlagAlreadyPending, outcome.Action);
        Assert.Empty(_flags.Staged);
    }

    [Theory]
    [InlineData(1, NonDeliveryAbuseAction.BelowThreshold)]
    [InlineData(2, NonDeliveryAbuseAction.Flagged)]
    [InlineData(4, NonDeliveryAbuseAction.Suspended)]
    public async Task Admin_Configured_Thresholds_Are_The_Ones_Applied(int priorEvents, NonDeliveryAbuseAction expected)
    {
        // Every other test runs on the seeded defaults (30 · 2 · 3), and a
        // version of the evaluator that hard-coded those numbers passed all of
        // them — the thresholds are admin settings (02 §16.2), so they need a
        // test whose values differ from the defaults. Here flag = 3 and
        // suspend = 5: two events are harmless (the default would flag) and
        // three only flag (the default would suspend).
        for (var i = 1; i <= priorEvents; i++)
            await InsertEventAsync(_seller.Id, (EventKind)(i % 3), daysAgo: i);
        var trigger = await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 0);

        var outcome = await BuildSut(new NonDeliveryAbuseThresholds(WindowDays: 10, FlagCount: 3, SuspendCount: 5))
            .EvaluateAsync(trigger.Id, CancellationToken.None);

        Assert.Equal(expected, outcome.Action);
        Assert.Equal(priorEvents + 1, outcome.EventCount);
    }

    [Fact]
    public async Task Admin_Configured_Window_Is_The_One_Applied()
    {
        // 12 and 14 days ago: inside the default 30-day window (which would
        // suspend on this third event), outside a configured 10-day one.
        await InsertEventAsync(_seller.Id, EventKind.DeliveryReversed, daysAgo: 12);
        await InsertEventAsync(_seller.Id, EventKind.SellerCancelAfterPayment, daysAgo: 14);
        var trigger = await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 0);

        var outcome = await BuildSut(new NonDeliveryAbuseThresholds(WindowDays: 10, FlagCount: 2, SuspendCount: 3))
            .EvaluateAsync(trigger.Id, CancellationToken.None);

        Assert.Equal(NonDeliveryAbuseAction.BelowThreshold, outcome.Action);
        Assert.Equal(1, outcome.EventCount);
    }

    [Fact]
    public async Task Events_Outside_The_Window_Do_Not_Count()
    {
        await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 31);
        await InsertEventAsync(_seller.Id, EventKind.DeliveryReversed, daysAgo: 45);
        var trigger = await InsertEventAsync(_seller.Id, EventKind.SellerCancelAfterPayment, daysAgo: 0);

        var outcome = await BuildSut().EvaluateAsync(trigger.Id, CancellationToken.None);

        Assert.Equal(NonDeliveryAbuseAction.BelowThreshold, outcome.Action);
        Assert.Equal(1, outcome.EventCount);
    }

    [Fact]
    public async Task Another_Sellers_Events_Do_Not_Count()
    {
        await InsertEventAsync(_otherSeller.Id, EventKind.DeliveryTimeout, daysAgo: 3);
        await InsertEventAsync(_otherSeller.Id, EventKind.DeliveryReversed, daysAgo: 2);
        var trigger = await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 0);

        var outcome = await BuildSut().EvaluateAsync(trigger.Id, CancellationToken.None);

        Assert.Equal(1, outcome.EventCount);
        Assert.Empty(_flags.Staged);
    }

    // ---- what does NOT count ----

    public enum NonEvent
    {
        TimeoutReleasedByAdminRuling,
        TimeoutBlockedByCounterparty,
        BuyerPaymentTimeout,
        SellerCancelBeforePayment,
        AdminCancel,
        DisputeRefund,
        BuyerCancel,
        Completed,
    }

    [Theory]
    [InlineData(NonEvent.TimeoutReleasedByAdminRuling)]
    [InlineData(NonEvent.TimeoutBlockedByCounterparty)]
    [InlineData(NonEvent.BuyerPaymentTimeout)]
    [InlineData(NonEvent.SellerCancelBeforePayment)]
    [InlineData(NonEvent.AdminCancel)]
    [InlineData(NonEvent.DisputeRefund)]
    [InlineData(NonEvent.BuyerCancel)]
    [InlineData(NonEvent.Completed)]
    public async Task A_Non_Event_Trigger_Does_Nothing_Even_Above_The_Threshold(NonEvent kind)
    {
        // Two real events are already in the window: if the trigger were counted
        // the seller would be suspended. Doing nothing is the only right answer.
        await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 10);
        await InsertEventAsync(_seller.Id, EventKind.DeliveryReversed, daysAgo: 5);
        var trigger = await InsertNonEventAsync(_seller.Id, kind, daysAgo: 0);

        var outcome = await BuildSut().EvaluateAsync(trigger.Id, CancellationToken.None);

        Assert.Equal(NonDeliveryAbuseAction.NotANonDeliveryEvent, outcome.Action);
        Assert.Empty(_flags.Staged);
        Assert.False((await ReloadSellerAsync()).IsSuspended);
    }

    [Theory]
    [InlineData(NonEvent.TimeoutReleasedByAdminRuling)]
    [InlineData(NonEvent.TimeoutBlockedByCounterparty)]
    [InlineData(NonEvent.SellerCancelBeforePayment)]
    [InlineData(NonEvent.DisputeRefund)]
    public async Task Excluded_Transitions_Are_Not_Counted_Toward_A_Real_Event(NonEvent kind)
    {
        await InsertNonEventAsync(_seller.Id, kind, daysAgo: 10);
        await InsertNonEventAsync(_seller.Id, kind, daysAgo: 5);
        var trigger = await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 0);

        var outcome = await BuildSut().EvaluateAsync(trigger.Id, CancellationToken.None);

        Assert.Equal(NonDeliveryAbuseAction.BelowThreshold, outcome.Action);
        Assert.Equal(1, outcome.EventCount);
    }

    // ---- idempotency and admin decisions ----

    [Fact]
    public async Task An_Unrelated_Completion_Does_Not_Resuspend_A_Seller_An_Admin_Cleared()
    {
        // Three events in the window and the seller is NOT suspended: an admin
        // lifted it. A later completion must leave that decision alone — the
        // sanction is keyed to a new failure, not to the window's contents.
        await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 12);
        await InsertEventAsync(_seller.Id, EventKind.SellerCancelAfterPayment, daysAgo: 8);
        await InsertEventAsync(_seller.Id, EventKind.DeliveryReversed, daysAgo: 4);
        var completed = await InsertNonEventAsync(_seller.Id, NonEvent.Completed, daysAgo: 0);

        var outcome = await BuildSut().EvaluateAsync(completed.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(NonDeliveryAbuseAction.NotANonDeliveryEvent, outcome.Action);
        Assert.False((await ReloadSellerAsync()).IsSuspended);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task Already_Suspended_Seller_Is_Not_Suspended_Twice()
    {
        var seller = await Context.Set<User>().SingleAsync(u => u.Id == _seller.Id);
        seller.IsSuspended = true;
        seller.SuspendedAt = _clock.GetUtcNow().UtcDateTime.AddDays(-1);
        seller.SuspensionReason = "Admin tarafından askıya alındı";
        await Context.SaveChangesAsync();

        await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 20);
        await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 10);
        var trigger = await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 0);

        var outcome = await BuildSut().EvaluateAsync(trigger.Id, CancellationToken.None);

        Assert.Equal(NonDeliveryAbuseAction.AlreadySuspended, outcome.Action);
        Assert.Empty(_audit.Entries);
        Assert.Equal("Admin tarafından askıya alındı", (await ReloadSellerAsync()).SuspensionReason);
    }

    [Fact]
    public async Task Two_Events_Evaluated_In_One_Unit_Of_Work_Suspend_Once()
    {
        // The deadline scanner flushes a whole batch before evaluating, so the
        // same seller can be evaluated twice with nothing saved in between. The
        // second evaluation must see the first one's (unsaved) suspension.
        await InsertEventAsync(_seller.Id, EventKind.DeliveryReversed, daysAgo: 20);
        var second = await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 0);
        var third = await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 0);
        var sut = BuildSut();

        var first = await sut.EvaluateAsync(second.Id, CancellationToken.None);
        var again = await sut.EvaluateAsync(third.Id, CancellationToken.None);
        await Context.SaveChangesAsync();

        Assert.Equal(NonDeliveryAbuseAction.Suspended, first.Action);
        Assert.Equal(NonDeliveryAbuseAction.AlreadySuspended, again.Action);
        Assert.Single(_audit.Entries);
        Assert.Single(_outbox.Events.OfType<AccountSuspendedEvent>());
    }

    [Theory]
    [InlineData(0, 2, 3)]
    [InlineData(30, 0, 3)]
    [InlineData(30, 2, 0)]
    public async Task Rule_Is_Disabled_When_Any_Threshold_Is_Unconfigured(int windowDays, int flagCount, int suspendCount)
    {
        await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 2);
        await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 1);
        var trigger = await InsertEventAsync(_seller.Id, EventKind.DeliveryTimeout, daysAgo: 0);

        var outcome = await BuildSut(new NonDeliveryAbuseThresholds(windowDays, flagCount, suspendCount))
            .EvaluateAsync(trigger.Id, CancellationToken.None);

        Assert.Equal(NonDeliveryAbuseAction.RuleDisabled, outcome.Action);
        Assert.Empty(_flags.Staged);
        Assert.False((await ReloadSellerAsync()).IsSuspended);
    }

    // ---- helpers ----

    private NonDeliveryAbuseEvaluator BuildSut(NonDeliveryAbuseThresholds? thresholds = null) => new(
        Context,
        new StubThresholds(thresholds ?? new NonDeliveryAbuseThresholds(WindowDays: 30, FlagCount: 2, SuspendCount: 3)),
        _flags,
        _flags,
        new UserSuspensionWriter(_audit, _outbox),
        _clock,
        NullLogger<NonDeliveryAbuseEvaluator>.Instance);

    private static EventKind Next(EventKind kind) => (EventKind)(((int)kind + 1) % 3);

    private async Task<User> ReloadSellerAsync() =>
        await Context.Set<User>().AsNoTracking().SingleAsync(u => u.Id == _seller.Id);

    private Task<Transaction> InsertEventAsync(Guid sellerId, EventKind kind, int daysAgo) => kind switch
    {
        EventKind.DeliveryTimeout => InsertAsync(
            sellerId, TransactionStatus.CANCELLED_TIMEOUT, daysAgo, previousStatus: TransactionStatus.PAYMENT_RECEIVED),
        EventKind.SellerCancelAfterPayment => InsertAsync(
            sellerId, TransactionStatus.CANCELLED_SELLER, daysAgo, previousStatus: TransactionStatus.PAYMENT_RECEIVED),
        _ => InsertAsync(sellerId, TransactionStatus.REFUNDED, daysAgo, previousStatus: null, deliveryReversed: true),
    };

    private Task<Transaction> InsertNonEventAsync(Guid sellerId, NonEvent kind, int daysAgo) => kind switch
    {
        NonEvent.TimeoutReleasedByAdminRuling => InsertAsync(
            sellerId, TransactionStatus.CANCELLED_TIMEOUT, daysAgo, TransactionStatus.PAYMENT_RECEIVED, releasedByAdminRuling: true),
        NonEvent.TimeoutBlockedByCounterparty => InsertAsync(
            sellerId, TransactionStatus.CANCELLED_TIMEOUT, daysAgo, TransactionStatus.PAYMENT_RECEIVED, blockedByCounterparty: true),
        NonEvent.BuyerPaymentTimeout => InsertAsync(
            sellerId, TransactionStatus.CANCELLED_TIMEOUT, daysAgo, TransactionStatus.SELLER_CONFIRMED),
        NonEvent.SellerCancelBeforePayment => InsertAsync(
            sellerId, TransactionStatus.CANCELLED_SELLER, daysAgo, TransactionStatus.ACCEPTED),
        NonEvent.AdminCancel => InsertAsync(
            sellerId, TransactionStatus.CANCELLED_ADMIN, daysAgo, TransactionStatus.PAYMENT_RECEIVED),
        NonEvent.DisputeRefund => InsertAsync(
            sellerId, TransactionStatus.REFUNDED, daysAgo, previousStatus: null, deliveryReversed: false),
        NonEvent.BuyerCancel => InsertAsync(
            sellerId, TransactionStatus.CANCELLED_BUYER, daysAgo, TransactionStatus.SELLER_CONFIRMED),
        _ => InsertAsync(sellerId, TransactionStatus.COMPLETED, daysAgo, previousStatus: null),
    };

    private async Task<Transaction> InsertAsync(
        Guid sellerId,
        TransactionStatus status,
        int daysAgo,
        TransactionStatus? previousStatus,
        bool releasedByAdminRuling = false,
        bool blockedByCounterparty = false,
        bool deliveryReversed = false)
    {
        var at = _clock.GetUtcNow().UtcDateTime.AddDays(-daysAgo);
        var isTerminalCancel = status is TransactionStatus.CANCELLED_TIMEOUT
            or TransactionStatus.CANCELLED_SELLER
            or TransactionStatus.CANCELLED_BUYER
            or TransactionStatus.CANCELLED_ADMIN
            or TransactionStatus.REFUNDED;

        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = sellerId,
            BuyerId = _buyer.Id,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = _buyer.SteamId,
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
            // CK_Transactions_Cancel: every cancel/refund terminal state carries
            // the full (CancelledBy, CancelReason, CancelledAt) trail.
            CancelledAt = isTerminalCancel ? at : null,
            CancelledBy = status switch
            {
                TransactionStatus.CANCELLED_TIMEOUT => CancelledByType.TIMEOUT,
                // A reversal is attributed to the seller by the state machine.
                TransactionStatus.CANCELLED_SELLER => CancelledByType.SELLER,
                TransactionStatus.REFUNDED => deliveryReversed ? CancelledByType.SELLER : CancelledByType.ADMIN,
                TransactionStatus.CANCELLED_BUYER => CancelledByType.BUYER,
                TransactionStatus.CANCELLED_ADMIN => CancelledByType.ADMIN,
                _ => null,
            },
            CancelReason = isTerminalCancel ? "test" : null,
            TimeoutReleasedByAdminRulingAt = releasedByAdminRuling ? at : null,
            TimeoutBlockedByCounterpartyAt = blockedByCounterparty ? at : null,
            DeliveryReversedAt = deliveryReversed ? at : null,
        };
        Context.Set<Transaction>().Add(tx);

        if (previousStatus is { } previous)
        {
            Context.Set<TransactionHistory>().Add(new TransactionHistory
            {
                TransactionId = tx.Id,
                PreviousStatus = previous,
                NewStatus = status,
                Trigger = "test",
                ActorType = ActorType.SYSTEM,
                ActorId = SeedConstants.SystemUserId,
                CreatedAt = at,
            });
        }

        await Context.SaveChangesAsync();
        return tx;
    }

    private sealed class StubThresholds : INonDeliveryAbuseThresholdsProvider
    {
        private readonly NonDeliveryAbuseThresholds _value;
        public StubThresholds(NonDeliveryAbuseThresholds value) => _value = value;
        public Task<NonDeliveryAbuseThresholds> GetAsync(CancellationToken cancellationToken) => Task.FromResult(_value);
    }

    /// <summary>
    /// Both flag ports in one double: the pending check answers from what this
    /// double staged (mirroring the real checker's view of unsaved flags) or
    /// from an explicitly pre-set pending flag.
    /// </summary>
    private sealed class FakeFlagPort : IAccountFlagChecker, ITransactionFraudFlagWriter
    {
        public bool PendingAbnormalBehavior { get; set; }

        public List<(Guid UserId, FraudFlagType Type, string Details)> Staged { get; } = [];

        public Task<bool> HasActiveAccountFlagAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<bool> HasPendingAccountFlagAsync(Guid userId, FraudFlagType type, CancellationToken cancellationToken)
            => Task.FromResult(
                (PendingAbnormalBehavior && type == FraudFlagType.ABNORMAL_BEHAVIOR)
                || Staged.Any(s => s.UserId == userId && s.Type == type));

        public Task StagePreCreateFlagAsync(
            Guid userId, Guid transactionId, FraudFlagType type, string details, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The non-delivery rule never writes pre-create flags.");

        public Task StageAccountFlagAsync(Guid userId, FraudFlagType type, string details, CancellationToken cancellationToken)
        {
            Staged.Add((userId, type, details));
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingAuditLogger : IAuditLogger
    {
        public List<AuditLogEntry> Entries { get; } = [];

        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingOutbox : IOutboxService
    {
        public List<IDomainEvent> Events { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }
}
