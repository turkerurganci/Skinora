using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Fraud.Application.Flags;
using Skinora.Fraud.Domain.Entities;
using Skinora.Fraud.Infrastructure.Persistence;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Fraud.Tests.Integration;

/// <summary>
/// Full integration coverage for <see cref="FraudFlagService"/> — the T54
/// staging + review pipeline against a real SQL Server. Pairs the audit
/// pipeline (<see cref="IAuditLogger"/>) with a recording outbox so each
/// invariant from 02 §14.0 / 03 §7–§8.2 / 07 §9.4–§9.5 is exercised
/// end-to-end:
/// <list type="bullet">
///   <item>Account flag staged with cascade → <c>EMERGENCY_HOLD</c> on every active tx.</item>
///   <item>Transaction pre-create flag staged inside a caller-owned SaveChanges.</item>
///   <item>Approve a transaction flag → <c>FLAGGED</c> → <c>CREATED</c> + new <c>AcceptDeadline</c>.</item>
///   <item>Reject a transaction flag → <c>FLAGGED</c> → <c>CANCELLED_ADMIN</c>.</item>
///   <item>Idempotency / 409 paths (already-reviewed, transaction-not-flagged).</item>
/// </list>
/// </summary>
public class FraudFlagServiceTests : IntegrationTestBase
{
    static FraudFlagServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        FraudModuleDbRegistration.RegisterFraudModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string ValidWallet = "TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL";

    private FakeTimeProvider _clock = null!;
    private RecordingOutboxService _outbox = null!;
    private User _seller = null!;
    private User _buyer = null!;
    private User _admin = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
        _outbox = new RecordingOutboxService();

        // SYSTEM user is created automatically via EF HasData in UserConfiguration
        // (06 §8.9, T26 seed) — no manual insert needed.
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198540000001",
            SteamDisplayName = "FlaggedSeller",
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198540000002",
            SteamDisplayName = "Buyer",
        };
        _admin = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198540000099",
            SteamDisplayName = "Admin",
        };
        context.Set<User>().AddRange(_seller, _buyer, _admin);
        await context.SaveChangesAsync();
    }

    // ── StageAccountFlagAsync ────────────────────────────────────────────

    [Fact]
    public async Task StageAccountFlag_NoCascade_PersistsFlag_AndAuditLog_AndOutbox()
    {
        var sut = BuildSut();

        var flagId = await sut.StageAccountFlagAsync(
            userId: _seller.Id,
            type: FraudFlagType.MULTI_ACCOUNT,
            details: "{\"matchType\":\"wallet\",\"matchValue\":\"TX...\"}",
            actorId: SeedConstants.SystemUserId,
            actorType: ActorType.SYSTEM,
            cascadeEmergencyHold: false,
            emergencyHoldReason: null,
            cancellationToken: CancellationToken.None);

        await Context.SaveChangesAsync();

        var flag = await Context.Set<FraudFlag>().FirstOrDefaultAsync(f => f.Id == flagId);
        Assert.NotNull(flag);
        Assert.Equal(FraudFlagScope.ACCOUNT_LEVEL, flag!.Scope);
        Assert.Equal(FraudFlagType.MULTI_ACCOUNT, flag.Type);
        Assert.Equal(ReviewStatus.PENDING, flag.Status);
        Assert.Null(flag.TransactionId);

        var audit = await Context.Set<AuditLog>()
            .FirstOrDefaultAsync(a => a.EntityId == flagId.ToString()
                                    && a.Action == AuditAction.FRAUD_FLAG_CREATED);
        Assert.NotNull(audit);
        Assert.Equal(ActorType.SYSTEM, audit!.ActorType);

        var evt = Assert.IsType<FraudFlagCreatedEvent>(_outbox.Published.Single());
        Assert.Equal(flagId, evt.FraudFlagId);
        Assert.False(evt.EmergencyHoldAppliedToActiveTransactions);
    }

    [Fact]
    public async Task StageAccountFlag_Cascade_FreezesActiveSellerTransactions()
    {
        var activeTx = await CreateActiveTransactionAsync(_seller.Id, buyerId: null);
        var completedTx = await CreateTransactionAsync(_seller.Id, buyerId: null,
            status: TransactionStatus.COMPLETED);
        var alreadyOnHold = await CreateActiveTransactionAsync(_seller.Id, buyerId: null,
            isOnHold: true);

        var sut = BuildSut();

        var flagId = await sut.StageAccountFlagAsync(
            userId: _seller.Id,
            type: FraudFlagType.ABNORMAL_BEHAVIOR,
            details: "{\"pattern\":\"sanctions\",\"description\":\"OFAC\"}",
            actorId: SeedConstants.SystemUserId,
            actorType: ActorType.SYSTEM,
            cascadeEmergencyHold: true,
            emergencyHoldReason: "Sanctions match — OFAC list",
            cancellationToken: CancellationToken.None);

        await Context.SaveChangesAsync();

        var evt = Assert.IsType<FraudFlagCreatedEvent>(_outbox.Published.Single());
        Assert.True(evt.EmergencyHoldAppliedToActiveTransactions);

        var refreshedActive = await Context.Set<Transaction>()
            .FirstAsync(t => t.Id == activeTx.Id);
        Assert.True(refreshedActive.IsOnHold);
        Assert.Equal("Sanctions match — OFAC list", refreshedActive.EmergencyHoldReason);
        Assert.Equal((int)TransactionStatus.CREATED, refreshedActive.PreviousStatusBeforeHold);

        var refreshedCompleted = await Context.Set<Transaction>()
            .FirstAsync(t => t.Id == completedTx.Id);
        Assert.False(refreshedCompleted.IsOnHold);

        var refreshedAlreadyOnHold = await Context.Set<Transaction>()
            .FirstAsync(t => t.Id == alreadyOnHold.Id);
        Assert.True(refreshedAlreadyOnHold.IsOnHold);
        // Reason wasn't overwritten — service skips already-on-hold txs.
        Assert.NotEqual("Sanctions match — OFAC list", refreshedAlreadyOnHold.EmergencyHoldReason);

        var holdAudits = await Context.Set<AuditLog>()
            .Where(a => a.Action == AuditAction.FRAUD_FLAG_AUTO_HOLD)
            .ToListAsync();
        Assert.Single(holdAudits);
        Assert.Equal(activeTx.Id.ToString(), holdAudits[0].EntityId);

        Assert.Equal(flagId, evt.FraudFlagId);
    }

    [Fact]
    public async Task StageAccountFlag_Cascade_FreezesBuyerSideTransactionsToo()
    {
        // Sanctions on a buyer wallet must also freeze transactions where
        // they are the buyer (03 §11a.3 cross-role trigger).
        var sellerSideTx = await CreateActiveTransactionAsync(_seller.Id, buyerId: _buyer.Id);

        var sut = BuildSut();

        await sut.StageAccountFlagAsync(
            userId: _buyer.Id,
            type: FraudFlagType.ABNORMAL_BEHAVIOR,
            details: "{}",
            actorId: SeedConstants.SystemUserId,
            actorType: ActorType.SYSTEM,
            cascadeEmergencyHold: true,
            emergencyHoldReason: "Buyer wallet matched sanctions",
            cancellationToken: CancellationToken.None);

        await Context.SaveChangesAsync();

        var refreshed = await Context.Set<Transaction>().FirstAsync(t => t.Id == sellerSideTx.Id);
        Assert.True(refreshed.IsOnHold);
    }

    [Fact]
    public async Task StageAccountFlag_Cascade_Without_Reason_Throws()
    {
        var sut = BuildSut();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.StageAccountFlagAsync(
                userId: _seller.Id,
                type: FraudFlagType.ABNORMAL_BEHAVIOR,
                details: "{}",
                actorId: SeedConstants.SystemUserId,
                actorType: ActorType.SYSTEM,
                cascadeEmergencyHold: true,
                emergencyHoldReason: "  ",
                cancellationToken: CancellationToken.None));
    }

    // ── StageTransactionFlagAsync ────────────────────────────────────────

    [Fact]
    public async Task StageTransactionFlag_PersistsLinkedFlag_AndOutbox()
    {
        var tx = await CreateTransactionAsync(_seller.Id, buyerId: null,
            status: TransactionStatus.FLAGGED);
        var sut = BuildSut();

        var flagId = await sut.StageTransactionFlagAsync(
            userId: _seller.Id,
            transactionId: tx.Id,
            type: FraudFlagType.PRICE_DEVIATION,
            details: "{\"inputPrice\":120,\"marketPrice\":50,\"deviationPercent\":140}",
            actorId: SeedConstants.SystemUserId,
            actorType: ActorType.SYSTEM,
            cancellationToken: CancellationToken.None);

        await Context.SaveChangesAsync();

        var flag = await Context.Set<FraudFlag>().FirstAsync(f => f.Id == flagId);
        Assert.Equal(FraudFlagScope.TRANSACTION_PRE_CREATE, flag.Scope);
        Assert.Equal(tx.Id, flag.TransactionId);
        Assert.Contains("inputPrice", flag.Details);

        var evt = Assert.IsType<FraudFlagCreatedEvent>(_outbox.Published.Single());
        Assert.Equal(tx.Id, evt.TransactionId);
        Assert.False(evt.EmergencyHoldAppliedToActiveTransactions);
    }

    // ── ApproveAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_TransactionFlag_PromotesFlaggedToCreated_AndSetsAcceptDeadline()
    {
        var tx = await CreateTransactionAsync(_seller.Id, buyerId: null,
            status: TransactionStatus.FLAGGED);
        var flag = await StageTransactionFlagDirectlyAsync(_seller.Id, tx.Id);

        var sut = BuildSut();
        var outcome = await sut.ApproveAsync(flag.Id, _admin.Id, "Looks fine",
            CancellationToken.None);

        var success = Assert.IsType<ApproveFlagOutcome.Success>(outcome);
        Assert.Equal(ReviewStatus.APPROVED, success.Result.ReviewStatus);
        Assert.Equal(TransactionStatus.CREATED, success.Result.TransactionStatus);

        var refreshedFlag = await Context.Set<FraudFlag>().FirstAsync(f => f.Id == flag.Id);
        Assert.Equal(ReviewStatus.APPROVED, refreshedFlag.Status);
        Assert.Equal(_admin.Id, refreshedFlag.ReviewedByAdminId);
        Assert.Equal("Looks fine", refreshedFlag.AdminNote);

        var refreshedTx = await Context.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.CREATED, refreshedTx.Status);
        Assert.NotNull(refreshedTx.AcceptDeadline);
        Assert.True(refreshedTx.AcceptDeadline > _clock.GetUtcNow().UtcDateTime);

        var audit = await Context.Set<AuditLog>()
            .FirstOrDefaultAsync(a => a.Action == AuditAction.FRAUD_FLAG_APPROVED
                                    && a.EntityId == flag.Id.ToString());
        Assert.NotNull(audit);
        Assert.Equal(ActorType.ADMIN, audit!.ActorType);
        Assert.Equal(_admin.Id, audit.ActorId);
    }

    [Fact]
    public async Task Approve_AccountFlag_Records_Review_Without_Touching_Transactions()
    {
        var flag = await StageAccountFlagDirectlyAsync(_seller.Id);
        var tx = await CreateActiveTransactionAsync(_seller.Id, buyerId: null);

        var sut = BuildSut();
        var outcome = await sut.ApproveAsync(flag.Id, _admin.Id, note: null,
            CancellationToken.None);

        var success = Assert.IsType<ApproveFlagOutcome.Success>(outcome);
        Assert.Null(success.Result.TransactionStatus);

        var refreshedTx = await Context.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);
        Assert.False(refreshedTx.IsOnHold);
        Assert.Equal(TransactionStatus.CREATED, refreshedTx.Status);
    }

    [Fact]
    public async Task Approve_NotFound_ReturnsNotFoundOutcome()
    {
        var sut = BuildSut();
        var outcome = await sut.ApproveAsync(Guid.NewGuid(), _admin.Id, null,
            CancellationToken.None);
        Assert.IsType<ApproveFlagOutcome.NotFound>(outcome);
    }

    [Fact]
    public async Task Approve_AlreadyReviewed_ReturnsAlreadyReviewedOutcome()
    {
        var flag = await StageAccountFlagDirectlyAsync(_seller.Id);
        flag.Status = ReviewStatus.APPROVED;
        flag.ReviewedAt = DateTime.UtcNow;
        flag.ReviewedByAdminId = _admin.Id;
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.ApproveAsync(flag.Id, _admin.Id, null,
            CancellationToken.None);
        Assert.IsType<ApproveFlagOutcome.AlreadyReviewed>(outcome);
    }

    [Fact]
    public async Task Approve_TransactionNotFlagged_Returns409Outcome()
    {
        var tx = await CreateTransactionAsync(_seller.Id, buyerId: null,
            status: TransactionStatus.CREATED);
        var flag = await StageTransactionFlagDirectlyAsync(_seller.Id, tx.Id);

        var sut = BuildSut();
        var outcome = await sut.ApproveAsync(flag.Id, _admin.Id, null,
            CancellationToken.None);
        Assert.IsType<ApproveFlagOutcome.TransactionNotFlagged>(outcome);

        // Flag should remain PENDING (rollback path).
        var refreshedFlag = await Context.Set<FraudFlag>().FirstAsync(f => f.Id == flag.Id);
        Assert.Equal(ReviewStatus.PENDING, refreshedFlag.Status);
    }

    // ── RejectAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task Reject_TransactionFlag_TransitionsToCancelledAdmin()
    {
        var tx = await CreateTransactionAsync(_seller.Id, buyerId: null,
            status: TransactionStatus.FLAGGED);
        var flag = await StageTransactionFlagDirectlyAsync(_seller.Id, tx.Id);

        var sut = BuildSut();
        var outcome = await sut.RejectAsync(flag.Id, _admin.Id, "Confirmed manipulation",
            CancellationToken.None);

        var success = Assert.IsType<RejectFlagOutcome.Success>(outcome);
        Assert.Equal(ReviewStatus.REJECTED, success.Result.ReviewStatus);
        Assert.Equal(TransactionStatus.CANCELLED_ADMIN, success.Result.TransactionStatus);

        var refreshedFlag = await Context.Set<FraudFlag>().FirstAsync(f => f.Id == flag.Id);
        Assert.Equal(ReviewStatus.REJECTED, refreshedFlag.Status);

        var refreshedTx = await Context.Set<Transaction>().FirstAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.CANCELLED_ADMIN, refreshedTx.Status);
        Assert.Equal("Flag reddedildi (admin)", refreshedTx.CancelReason);
    }

    [Fact]
    public async Task Reject_AccountFlag_Records_Review_Without_Transition()
    {
        var flag = await StageAccountFlagDirectlyAsync(_seller.Id);
        var sut = BuildSut();

        var outcome = await sut.RejectAsync(flag.Id, _admin.Id, "Confirmed", CancellationToken.None);
        var success = Assert.IsType<RejectFlagOutcome.Success>(outcome);
        Assert.Null(success.Result.TransactionStatus);

        var refreshedFlag = await Context.Set<FraudFlag>().FirstAsync(f => f.Id == flag.Id);
        Assert.Equal(ReviewStatus.REJECTED, refreshedFlag.Status);
        Assert.Equal("Confirmed", refreshedFlag.AdminNote);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private FraudFlagService BuildSut()
    {
        var auditLogger = new AuditLogger(Context, _clock);
        var limits = new TransactionLimitsProvider(Context);
        var scheduler = new NoopJobScheduler();
        var scheduling = new TimeoutSchedulingService(Context, scheduler, _clock);
        var freeze = new TimeoutFreezeService(Context, scheduler, scheduling, _clock);
        return new FraudFlagService(Context, auditLogger, _outbox, limits, freeze, _clock);
    }

    private async Task<Transaction> CreateActiveTransactionAsync(
        Guid sellerId, Guid? buyerId, bool isOnHold = false)
    {
        return await CreateTransactionAsync(
            sellerId, buyerId, TransactionStatus.CREATED, isOnHold: isOnHold);
    }

    private async Task<Transaction> CreateTransactionAsync(
        Guid sellerId,
        Guid? buyerId,
        TransactionStatus status,
        bool isOnHold = false)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = sellerId,
            BuyerId = buyerId,
            BuyerIdentificationMethod = BuyerIdentificationMethod.STEAM_ID,
            TargetBuyerSteamId = "76561198550000123",
            // ItemAssetId column is HasMaxLength(20); use a 12-digit numeric id
            // built from the Guid to keep helpers per-test unique.
            ItemAssetId = Guid.NewGuid().GetHashCode().ToString("X").PadLeft(8, '0') + "test",
            ItemClassId = "class-1",
            ItemName = "Test Item",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.03m,
            CommissionAmount = 3m,
            TotalAmount = 103m,
            SellerPayoutAddress = ValidWallet,
            PaymentTimeoutMinutes = 60,
            AcceptDeadline = status == TransactionStatus.FLAGGED
                ? null
                : nowUtc + TimeSpan.FromMinutes(60),
            IsOnHold = isOnHold,
            EmergencyHoldAt = isOnHold ? nowUtc : null,
            EmergencyHoldReason = isOnHold ? "previous hold" : null,
            EmergencyHoldByAdminId = isOnHold ? _admin.Id : null,
            PreviousStatusBeforeHold = isOnHold ? (int)status : null,
            // CK_Transactions_FreezeHold_Reverse + CK_Transactions_FreezeActive
            // require these freeze tracking fields whenever IsOnHold = 1.
            TimeoutFrozenAt = isOnHold ? nowUtc : null,
            TimeoutFreezeReason = isOnHold ? Skinora.Shared.Enums.TimeoutFreezeReason.EMERGENCY_HOLD : null,
            TimeoutRemainingSeconds = isOnHold ? 0 : null,
            CompletedAt = status == TransactionStatus.COMPLETED ? nowUtc : null,
            CancelledAt = status is TransactionStatus.CANCELLED_ADMIN
                       or TransactionStatus.CANCELLED_TIMEOUT
                       or TransactionStatus.CANCELLED_SELLER
                       or TransactionStatus.CANCELLED_BUYER ? nowUtc : null,
        };
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx;
    }

    private async Task<FraudFlag> StageAccountFlagDirectlyAsync(Guid userId)
    {
        var flag = new FraudFlag
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TransactionId = null,
            Scope = FraudFlagScope.ACCOUNT_LEVEL,
            Type = FraudFlagType.ABNORMAL_BEHAVIOR,
            Status = ReviewStatus.PENDING,
            Details = "{}",
        };
        Context.Set<FraudFlag>().Add(flag);
        await Context.SaveChangesAsync();
        return flag;
    }

    private async Task<FraudFlag> StageTransactionFlagDirectlyAsync(
        Guid userId, Guid transactionId)
    {
        var flag = new FraudFlag
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TransactionId = transactionId,
            Scope = FraudFlagScope.TRANSACTION_PRE_CREATE,
            Type = FraudFlagType.PRICE_DEVIATION,
            Status = ReviewStatus.PENDING,
            Details = JsonSerializer.Serialize(new
            {
                inputPrice = 120m,
                marketPrice = 50m,
                deviationPercent = 140m,
            }),
        };
        Context.Set<FraudFlag>().Add(flag);
        await Context.SaveChangesAsync();
        return flag;
    }

    private sealed class RecordingOutboxService : IOutboxService
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Published.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// No-op stub for <see cref="IBackgroundJobScheduler"/> — Hangfire is out
    /// of scope for these tests. <c>TimeoutFreezeService</c> calls
    /// <c>Delete</c> on existing job ids when freezing; returning a stable id
    /// from <c>Schedule</c> / <c>Enqueue</c> keeps the contract honoured
    /// without any actual scheduling.
    /// </summary>
    private sealed class NoopJobScheduler : IBackgroundJobScheduler
    {
        public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
            => Guid.NewGuid().ToString("N");
        public string Enqueue<T>(Expression<Action<T>> methodCall)
            => Guid.NewGuid().ToString("N");
        public bool Delete(string jobId) => true;
        public void AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>> methodCall, string cronExpression) { }
    }
}
