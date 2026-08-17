using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Skinora.Platform.Application.Audit;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Persistence;
using Skinora.Shared.Tests.Integration;
using Skinora.Transactions.Application.Admin;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Infrastructure.Persistence;
using Skinora.Transactions.Tests.Integration.Timeouts;
using Skinora.Users.Domain.Entities;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.Transactions.Tests.Integration.Admin;

/// <summary>
/// End-to-end coverage for <see cref="AdminTransactionService"/> (T59 —
/// 07 §9.20–§9.22, 02 §7, 03 §8.8). Exercises the orchestrator against the
/// shared SQL Server fixture so the freeze CK constraints + state machine
/// invariants are enforced in flight (06 §3.5 + 09 §13.3).
/// </summary>
public class AdminTransactionServiceTests : IntegrationTestBase
{
    static AdminTransactionServiceTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        TransactionsModuleDbRegistration.RegisterTransactionsModule();
        Skinora.Platform.Infrastructure.Persistence.PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    private const string ValidWallet = "TXyzABCDEFGHJKLMNPQRSTUVWXYZ234567";
    private const string BuyerWallet = "TabcDEFGHJKLMNPQRSTUVWXYZ234567Xyz";

    private FakeTimeProvider _clock = null!;
    private CapturingJobScheduler _scheduler = null!;
    private CapturingOutboxService _outbox = null!;
    private User _seller = null!;
    private User _buyer = null!;
    private User _admin = null!;

    protected override async Task SeedAsync(AppDbContext context)
    {
        _seller = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198100000001",
            SteamDisplayName = "Seller",
        };
        _buyer = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198100000002",
            SteamDisplayName = "Buyer",
        };
        _admin = new User
        {
            Id = Guid.NewGuid(),
            SteamId = "76561198100000003",
            SteamDisplayName = "Admin",
        };
        context.Set<User>().AddRange(_seller, _buyer, _admin);
        await context.SaveChangesAsync();

        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 5, 12, 0, 0, TimeSpan.Zero));
        _scheduler = new CapturingJobScheduler();
        _outbox = new CapturingOutboxService();
    }

    private AdminTransactionService BuildSut()
    {
        var scheduling = new TimeoutSchedulingService(Context, _scheduler, _clock);
        var freeze = new TimeoutFreezeService(Context, _scheduler, scheduling, _clock);
        var audit = new AuditLogger(Context, _clock);
        return new AdminTransactionService(
            Context, _outbox, audit, scheduling, freeze,
            new Skinora.Transactions.Tests.Helpers.NoOpPostCancelMonitorStarter(),
            _clock);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  AD19 — POST /admin/transactions/:id/cancel
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelAsync_From_Created_Cancels_Without_Refund_Events()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.CREATED, withBuyer: false);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _admin.Id, tx.Id,
            new AdminCancelTransactionRequest("Yasal talep — admin iptal"),
            ipAddress: "127.0.0.1",
            CancellationToken.None);

        Assert.Equal(AdminCancelTransactionStatus.Cancelled, outcome.Status);
        Assert.NotNull(outcome.Body);
        Assert.Equal(TransactionStatus.CANCELLED_ADMIN, outcome.Body.Status);
        Assert.False(outcome.Body.PaymentRefunded);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.CANCELLED_ADMIN, persisted.Status);
        Assert.Equal(CancelledByType.ADMIN, persisted.CancelledBy);
        Assert.Equal("Yasal talep — admin iptal", persisted.CancelReason);

        Assert.Empty(_outbox.Published.OfType<PaymentRefundToBuyerRequestedEvent>());
        var cancelEvent = Assert.Single(_outbox.Published.OfType<TransactionCancelledEvent>());
        Assert.Equal(CancelledByType.ADMIN, cancelEvent.CancelledBy);
    }

    [Fact]
    public async Task CancelAsync_From_SellerConfirmed_Emits_No_Refund_Events()
    {
        // v3.0 — nothing has moved at SELLER_CONFIRMED: the item is still with
        // the seller and the buyer has not paid (02 §9).
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _admin.Id, tx.Id,
            new AdminCancelTransactionRequest("İşlem yüksek risk taşıyor"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(AdminCancelTransactionStatus.Cancelled, outcome.Status);
        Assert.False(outcome.Body!.PaymentRefunded);

        Assert.Empty(_outbox.Published.OfType<PaymentRefundToBuyerRequestedEvent>());
    }

    [Fact]
    public async Task CancelAsync_From_PaymentReceived_Emits_Payment_Refund_Only()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _admin.Id, tx.Id,
            new AdminCancelTransactionRequest("Sanctions list match"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(AdminCancelTransactionStatus.Cancelled, outcome.Status);
        Assert.True(outcome.Body!.PaymentRefunded);

        var paymentRefund = Assert.Single(_outbox.Published.OfType<PaymentRefundToBuyerRequestedEvent>());
        Assert.Equal(_buyer.Id, paymentRefund.BuyerId);
        Assert.Equal(BuyerWallet, paymentRefund.BuyerRefundAddress);
    }

    [Fact]
    public async Task CancelAsync_ITEM_DELIVERED_Returns_422_CannotCancelAtDeliveryStage()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _admin.Id, tx.Id,
            new AdminCancelTransactionRequest("Item zaten teslim — yine de denenirse"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(AdminCancelTransactionStatus.CannotCancelAtDeliveryStage, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.CannotCancelAtDeliveryStage, outcome.ErrorCode);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persisted.Status);
        Assert.Empty(_outbox.Published);
    }

    [Theory]
    [InlineData(TransactionStatus.COMPLETED)]
    [InlineData(TransactionStatus.CANCELLED_ADMIN)]
    [InlineData(TransactionStatus.CANCELLED_TIMEOUT)]
    public async Task CancelAsync_Terminal_Returns_409_InvalidStateTransition(TransactionStatus status)
    {
        var tx = await CreateTransactionAsync(status, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _admin.Id, tx.Id,
            new AdminCancelTransactionRequest("Terminal state üzerinde"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(AdminCancelTransactionStatus.InvalidStateTransition, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.InvalidStateTransition, outcome.ErrorCode);
    }

    [Fact]
    public async Task CancelAsync_OnHold_Returns_409_InvalidStateTransition()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED, withBuyer: true);
        // Apply hold inline (admin must release first per orchestrator guard).
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        tx.IsOnHold = true;
        tx.EmergencyHoldAt = nowUtc;
        tx.EmergencyHoldReason = "pre-existing hold";
        tx.EmergencyHoldByAdminId = _admin.Id;
        tx.PreviousStatusBeforeHold = (int)tx.Status;
        tx.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        tx.TimeoutFrozenAt = nowUtc;
        tx.TimeoutRemainingSeconds = 600;
        Context.Update(tx);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _admin.Id, tx.Id,
            new AdminCancelTransactionRequest("Hold'lu işlem üzerinde direkt iptal"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(AdminCancelTransactionStatus.InvalidStateTransition, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.InvalidStateTransition, outcome.ErrorCode);
    }

    [Fact]
    public async Task CancelAsync_Reason_Below_Min_Returns_400_Validation()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.CREATED, withBuyer: false);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _admin.Id, tx.Id,
            new AdminCancelTransactionRequest("kısa"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(AdminCancelTransactionStatus.ValidationFailed, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.ValidationError, outcome.ErrorCode);
    }

    [Fact]
    public async Task CancelAsync_NotFound_Returns_404()
    {
        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _admin.Id, Guid.NewGuid(),
            new AdminCancelTransactionRequest("Geçersiz işlem ID — admin"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(AdminCancelTransactionStatus.NotFound, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.TransactionNotFound, outcome.ErrorCode);
    }

    [Fact]
    public async Task CancelAsync_Writes_TRANSACTION_CANCELLED_ADMIN_AuditRow()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ACCEPTED, withBuyer: true);

        var sut = BuildSut();
        await sut.CancelAsync(
            _admin.Id, tx.Id,
            new AdminCancelTransactionRequest("Audit row kontrolü"),
            ipAddress: "10.0.0.1",
            CancellationToken.None);

        var auditRow = await Context.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityType == nameof(Transaction)
                        && a.EntityId == tx.Id.ToString()
                        && a.Action == AuditAction.TRANSACTION_CANCELLED_ADMIN)
            .SingleAsync();
        Assert.Equal(_admin.Id, auditRow.ActorId);
        Assert.Equal(ActorType.ADMIN, auditRow.ActorType);
        Assert.Equal("10.0.0.1", auditRow.IpAddress);
        Assert.NotNull(auditRow.NewValue);
        Assert.Contains("CANCELLED_ADMIN", auditRow.NewValue);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  AD19b — POST /admin/transactions/:id/emergency-hold
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyEmergencyHoldAsync_Stamps_Hold_Fields_And_Cancels_Hangfire_Jobs()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.SELLER_CONFIRMED, withBuyer: true);
        // Pretend the timeout pipeline scheduled jobs at hold-time.
        tx.PaymentTimeoutJobId = "payment-job-001";
        tx.TimeoutWarningJobId = "warning-job-001";
        Context.Update(tx);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.ApplyEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ApplyEmergencyHoldRequest("Sanctions screening eşleşmesi tespit edildi"),
            ipAddress: "127.0.0.1",
            CancellationToken.None);

        Assert.Equal(ApplyEmergencyHoldStatus.Applied, outcome.Status);
        Assert.Equal("EMERGENCY_HOLD", outcome.Body!.Status);
        Assert.Equal(TransactionStatus.SELLER_CONFIRMED, outcome.Body.PreviousStatus);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.True(persisted.IsOnHold);
        Assert.Equal(_admin.Id, persisted.EmergencyHoldByAdminId);
        Assert.Equal("Sanctions screening eşleşmesi tespit edildi", persisted.EmergencyHoldReason);
        Assert.Equal((int)TransactionStatus.SELLER_CONFIRMED, persisted.PreviousStatusBeforeHold);
        Assert.Equal(TimeoutFreezeReason.EMERGENCY_HOLD, persisted.TimeoutFreezeReason);
        Assert.NotNull(persisted.TimeoutFrozenAt);
        Assert.Null(persisted.PaymentTimeoutJobId);
        Assert.Null(persisted.TimeoutWarningJobId);

        Assert.Contains("payment-job-001", _scheduler.DeletedJobIds);
        Assert.Contains("warning-job-001", _scheduler.DeletedJobIds);

        var holdEvent = Assert.Single(_outbox.Published.OfType<EmergencyHoldAppliedEvent>());
        Assert.Equal(_seller.Id, holdEvent.SellerId);
        Assert.Equal(_buyer.Id, holdEvent.BuyerId);

        var auditRow = await Context.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == AuditAction.EMERGENCY_HOLD_APPLIED
                              && a.EntityId == tx.Id.ToString());
        Assert.Equal(_admin.Id, auditRow.ActorId);
    }

    [Fact]
    public async Task ApplyEmergencyHoldAsync_AlreadyOnHold_Returns_409()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED, withBuyer: true);
        // Pre-stamp the hold invariants so the second apply hits the guard.
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        tx.IsOnHold = true;
        tx.EmergencyHoldAt = nowUtc;
        tx.EmergencyHoldReason = "first hold";
        tx.EmergencyHoldByAdminId = _admin.Id;
        tx.PreviousStatusBeforeHold = (int)tx.Status;
        tx.TimeoutFreezeReason = TimeoutFreezeReason.EMERGENCY_HOLD;
        tx.TimeoutFrozenAt = nowUtc;
        tx.TimeoutRemainingSeconds = 600;
        Context.Update(tx);
        await Context.SaveChangesAsync();

        var sut = BuildSut();
        var outcome = await sut.ApplyEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ApplyEmergencyHoldRequest("Tekrar hold uygulamayı dene"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ApplyEmergencyHoldStatus.AlreadyOnHold, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.AlreadyOnHold, outcome.ErrorCode);
    }

    [Fact]
    public async Task ApplyEmergencyHoldAsync_Terminal_Returns_409_InvalidStateTransition()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.COMPLETED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.ApplyEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ApplyEmergencyHoldRequest("Tamamlanmış işleme hold dene"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ApplyEmergencyHoldStatus.InvalidStateTransition, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.InvalidStateTransition, outcome.ErrorCode);
    }

    [Fact]
    public async Task ApplyEmergencyHoldAsync_Reason_Below_Min_Returns_400_Validation()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.CREATED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.ApplyEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ApplyEmergencyHoldRequest("kısa"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ApplyEmergencyHoldStatus.ValidationFailed, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.ValidationError, outcome.ErrorCode);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  AD19c — POST /admin/transactions/:id/release-hold
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReleaseEmergencyHoldAsync_Resume_From_PaymentReceived_Restores_Status()
    {
        // Apply hold first via the orchestrator so we exercise the real
        // freeze trio + Hangfire-cancel path.
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED, withBuyer: true);
        var sut = BuildSut();
        await sut.ApplyEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ApplyEmergencyHoldRequest("İlk hold uygulaması"),
            ipAddress: null,
            CancellationToken.None);
        _outbox.Published.Clear();

        var outcome = await sut.ReleaseEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ReleaseEmergencyHoldRequest(EmergencyHoldReleaseAction.RESUME, "İnceleme temiz, devam et"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ReleaseEmergencyHoldStatus.Released, outcome.Status);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, outcome.Body!.Status);
        Assert.Equal(EmergencyHoldReleaseAction.RESUME, outcome.Body.Action);
        Assert.Null(outcome.Body.PaymentRefunded);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.False(persisted.IsOnHold);
        Assert.Null(persisted.TimeoutFrozenAt);
        Assert.Null(persisted.TimeoutFreezeReason);
        Assert.Null(persisted.TimeoutRemainingSeconds);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, persisted.Status);

        var releasedEvent = Assert.Single(_outbox.Published.OfType<EmergencyHoldReleasedEvent>());
        Assert.Equal(EmergencyHoldReleaseAction.RESUME, releasedEvent.Action);
        Assert.Equal(TransactionStatus.PAYMENT_RECEIVED, releasedEvent.ResumedStatus);
    }

    [Fact]
    public async Task ReleaseEmergencyHoldAsync_Cancel_From_PaymentReceived_Hold_Transitions_To_AdminCancel_With_Payment_Refund()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED, withBuyer: true);
        var sut = BuildSut();
        await sut.ApplyEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ApplyEmergencyHoldRequest("İlk hold (cancel test)"),
            ipAddress: null,
            CancellationToken.None);
        _outbox.Published.Clear();

        var outcome = await sut.ReleaseEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ReleaseEmergencyHoldRequest(EmergencyHoldReleaseAction.CANCEL, "Soruşturma sonucu iptal"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ReleaseEmergencyHoldStatus.Released, outcome.Status);
        Assert.Equal(TransactionStatus.CANCELLED_ADMIN, outcome.Body!.Status);
        Assert.Equal(EmergencyHoldReleaseAction.CANCEL, outcome.Body.Action);
        Assert.True(outcome.Body.PaymentRefunded);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.False(persisted.IsOnHold);
        Assert.Equal(TransactionStatus.CANCELLED_ADMIN, persisted.Status);
        Assert.Null(persisted.TimeoutRemainingSeconds);
        Assert.Equal(CancelledByType.ADMIN, persisted.CancelledBy);
        Assert.NotNull(persisted.CancelReason);
        Assert.Contains("Soruşturma sonucu iptal", persisted.CancelReason);

        Assert.Single(_outbox.Published.OfType<PaymentRefundToBuyerRequestedEvent>());
        var cancelEvent = Assert.Single(_outbox.Published.OfType<TransactionCancelledEvent>());
        Assert.Equal(CancelledByType.ADMIN, cancelEvent.CancelledBy);
        // No EmergencyHoldReleased event for the CANCEL branch — the cancel
        // event covers user notification, the release row only lives in the
        // audit log.
        Assert.Empty(_outbox.Published.OfType<EmergencyHoldReleasedEvent>());

        var auditCount = await Context.Set<AuditLog>().AsNoTracking()
            .CountAsync(a => a.EntityId == tx.Id.ToString()
                             && (a.Action == AuditAction.EMERGENCY_HOLD_RELEASED
                                 || a.Action == AuditAction.TRANSACTION_CANCELLED_ADMIN));
        Assert.Equal(2, auditCount);
    }

    [Fact]
    public async Task ReleaseEmergencyHoldAsync_Cancel_From_ITEM_DELIVERED_Hold_Returns_422_CannotCancelDeliveredHold()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withBuyer: true);
        var sut = BuildSut();
        await sut.ApplyEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ApplyEmergencyHoldRequest("Item delivered hold (gözden geçirme)"),
            ipAddress: null,
            CancellationToken.None);
        _outbox.Published.Clear();

        var outcome = await sut.ReleaseEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ReleaseEmergencyHoldRequest(EmergencyHoldReleaseAction.CANCEL, "Yine de iptal denemesi"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ReleaseEmergencyHoldStatus.CannotCancelDeliveredHold, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.CannotCancelDeliveredHold, outcome.ErrorCode);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.True(persisted.IsOnHold);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persisted.Status);
    }

    [Fact]
    public async Task ReleaseEmergencyHoldAsync_Resume_From_ITEM_DELIVERED_Hold_Is_Allowed()
    {
        // Counterpart to the CANCEL guard — RESUME from ITEM_DELIVERED is
        // explicitly permitted (07 §9.22 + 03 §8.8).
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withBuyer: true);
        var sut = BuildSut();
        await sut.ApplyEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ApplyEmergencyHoldRequest("Item delivered hold — sonra resume"),
            ipAddress: null,
            CancellationToken.None);
        _outbox.Published.Clear();

        var outcome = await sut.ReleaseEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ReleaseEmergencyHoldRequest(EmergencyHoldReleaseAction.RESUME, "İnceleme tamam"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ReleaseEmergencyHoldStatus.Released, outcome.Status);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, outcome.Body!.Status);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.False(persisted.IsOnHold);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persisted.Status);
    }

    [Fact]
    public async Task ReleaseEmergencyHoldAsync_Not_On_Hold_Returns_409()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ACCEPTED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.ReleaseEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ReleaseEmergencyHoldRequest(EmergencyHoldReleaseAction.RESUME, "Hold yok ama deniyor"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ReleaseEmergencyHoldStatus.NotOnHold, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.NotOnHold, outcome.ErrorCode);
    }

    [Fact]
    public async Task ReleaseEmergencyHoldAsync_Empty_Note_Returns_400_Validation()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.CREATED, withBuyer: true);
        var sut = BuildSut();
        await sut.ApplyEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ApplyEmergencyHoldRequest("Hold ile birlikte note testi"),
            ipAddress: null,
            CancellationToken.None);

        var outcome = await sut.ReleaseEmergencyHoldAsync(
            _admin.Id, tx.Id,
            new ReleaseEmergencyHoldRequest(EmergencyHoldReleaseAction.RESUME, "    "),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ReleaseEmergencyHoldStatus.ValidationFailed, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.ValidationError, outcome.ErrorCode);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  AD19d — POST /admin/transactions/hold-by-user/:userId
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HoldAllUserTransactionsAsync_Holds_Active_And_Skips_Held_And_Terminal()
    {
        var active1 = await CreateTransactionAsync(TransactionStatus.CREATED, withBuyer: true);
        var active2 = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED, withBuyer: true);
        var preHeld = await CreateTransactionAsync(TransactionStatus.ACCEPTED, withBuyer: true);
        var terminal = await CreateTransactionAsync(TransactionStatus.COMPLETED, withBuyer: true);

        var sut = BuildSut();
        // Pre-hold one transaction individually — the bulk call must skip it.
        await sut.ApplyEmergencyHoldAsync(
            _admin.Id, preHeld.Id,
            new ApplyEmergencyHoldRequest("Önceden uygulanmış hold"),
            ipAddress: null,
            CancellationToken.None);

        var outcome = await sut.HoldAllUserTransactionsAsync(
            _admin.Id, _seller.Id,
            new HoldUserTransactionsRequest("Çoklu hesap — tüm aktif işlemler donduruldu"),
            ipAddress: "127.0.0.1",
            CancellationToken.None);

        Assert.Equal(HoldUserTransactionsStatus.Applied, outcome.Status);
        Assert.Equal(2, outcome.Body!.HeldCount);
        Assert.Contains(active1.Id, outcome.Body.HeldTransactionIds);
        Assert.Contains(active2.Id, outcome.Body.HeldTransactionIds);
        Assert.DoesNotContain(preHeld.Id, outcome.Body.HeldTransactionIds);
        Assert.DoesNotContain(terminal.Id, outcome.Body.HeldTransactionIds);

        var persisted = await Context.Set<Transaction>().AsNoTracking()
            .Where(t => t.Id == active1.Id || t.Id == active2.Id || t.Id == terminal.Id)
            .ToListAsync();
        Assert.True(persisted.Single(t => t.Id == active1.Id).IsOnHold);
        Assert.True(persisted.Single(t => t.Id == active2.Id).IsOnHold);
        Assert.False(persisted.Single(t => t.Id == terminal.Id).IsOnHold);

        // Each newly-held transaction emits its own EMERGENCY_HOLD_APPLIED audit row.
        var auditCount = await Context.Set<AuditLog>().AsNoTracking()
            .CountAsync(a => a.Action == AuditAction.EMERGENCY_HOLD_APPLIED
                             && (a.EntityId == active1.Id.ToString()
                                 || a.EntityId == active2.Id.ToString()));
        Assert.Equal(2, auditCount);
    }

    [Fact]
    public async Task HoldAllUserTransactionsAsync_Reason_Below_Min_Returns_Validation()
    {
        await CreateTransactionAsync(TransactionStatus.CREATED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.HoldAllUserTransactionsAsync(
            _admin.Id, _seller.Id,
            new HoldUserTransactionsRequest("kısa"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(HoldUserTransactionsStatus.ValidationFailed, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.ValidationError, outcome.ErrorCode);
    }

    [Fact]
    public async Task HoldAllUserTransactionsAsync_No_Active_Transactions_Returns_Zero()
    {
        // Only a terminal transaction exists for the user → nothing to hold.
        await CreateTransactionAsync(TransactionStatus.COMPLETED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.HoldAllUserTransactionsAsync(
            _admin.Id, _seller.Id,
            new HoldUserTransactionsRequest("Aktif işlem yok ama hold dene"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(HoldUserTransactionsStatus.Applied, outcome.Status);
        Assert.Equal(0, outcome.Body!.HeldCount);
        Assert.Empty(outcome.Body.HeldTransactionIds);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  AD32 — POST /admin/transactions/:id/clear-settlement (T129 fix round)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The lever that did not exist before the fix round. An escalated
    /// settlement had no terminating path at all: the payout, the sweep and the
    /// COMPLETED guard all wait on <c>SettlementVerifiedAt</c>, whose only
    /// writer was a check that could not conclude, and the one admin route named
    /// in the runbook (<c>admin_resolve_refund</c>) is reachable only through a
    /// dispute the buyer alone can open (validator finding B1).
    /// </summary>
    [Fact]
    public async Task ClearSettlementAsync_On_Escalated_Delivery_Stamps_Clearance_With_History_And_Audit()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withBuyer: true,
            configure: t =>
            {
                t.PayoutEligibleAt = nowUtc.AddDays(-1);
                t.SettlementCheckedAt = nowUtc.AddMinutes(-5);
                t.SettlementEscalatedAt = nowUtc.AddHours(-2);
                t.SettlementEscalationReason = SettlementReviewReasons.NoDeliveryReference;
            });

        var sut = BuildSut();
        var outcome = await sut.ClearSettlementAsync(
            _admin.Id, tx.Id,
            new ClearSettlementRequest("Steam trade geçmişi temiz, teslimat doğrulandı"),
            ipAddress: "127.0.0.1",
            CancellationToken.None);

        Assert.Equal(ClearSettlementStatus.Cleared, outcome.Status);
        Assert.Equal(SettlementReviewReasons.NoDeliveryReference, outcome.Body!.EscalationReason);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(nowUtc, persisted.SettlementVerifiedAt);
        Assert.Equal(_admin.Id, persisted.SettlementClearedByAdminId);
        Assert.Null(persisted.DeliveryReversedAt);

        // The status is deliberately untouched: COMPLETED still has to arrive
        // behind the payout, not in front of it.
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, persisted.Status);

        // WP15 history row — no transition, so a label rather than a trigger.
        var history = await Context.Set<TransactionHistory>().AsNoTracking()
            .SingleAsync(h => h.TransactionId == tx.Id
                && h.Trigger == AdminTransactionService.ClearSettlementTrigger);
        Assert.Equal(ActorType.ADMIN, history.ActorType);
        Assert.Equal(_admin.Id, history.ActorId);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, history.PreviousStatus);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, history.NewStatus);
        Assert.Contains(SettlementReviewReasons.NoDeliveryReference, history.AdditionalData);

        var audit = await Context.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.EntityId == tx.Id.ToString()
                && a.Action == AuditAction.SETTLEMENT_CLEARED_ADMIN);
        Assert.Equal(ActorType.ADMIN, audit.ActorType);
        Assert.Equal(_admin.Id, audit.ActorId);
        Assert.Contains(SettlementReviewReasons.NoDeliveryReference, audit.OldValue);

        // Read the payload back rather than substring-matching it: the
        // serializer escapes non-ASCII, so a Turkish reason never appears
        // literally in the stored JSON.
        var newValue = JsonSerializer.Deserialize<JsonElement>(audit.NewValue!);
        Assert.Equal(
            "Steam trade geçmişi temiz, teslimat doğrulandı",
            newValue.GetProperty("Reason").GetString());
        Assert.Equal(_admin.Id, newValue.GetProperty("ClearedBy").GetGuid());
    }

    [Fact]
    public async Task ClearSettlementAsync_Without_An_Escalation_Is_Refused()
    {
        // The admin ends what the platform asked about. Without this guard the
        // endpoint would be a way to pay a seller before the reversal window the
        // check exists to enforce has even elapsed.
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withBuyer: true,
            configure: t => t.PayoutEligibleAt = nowUtc.AddDays(7));

        var sut = BuildSut();
        var outcome = await sut.ClearSettlementAsync(
            _admin.Id, tx.Id,
            new ClearSettlementRequest("Erkenden ödemeyi açmayı dene"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ClearSettlementStatus.NotEscalated, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.SettlementNotEscalated, outcome.ErrorCode);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Null(persisted.SettlementVerifiedAt);
    }

    [Fact]
    public async Task ClearSettlementAsync_On_An_Already_Resolved_Settlement_Is_Refused()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withBuyer: true,
            configure: t =>
            {
                t.PayoutEligibleAt = nowUtc.AddDays(-1);
                t.SettlementEscalatedAt = nowUtc.AddHours(-2);
                t.SettlementEscalationReason = SettlementReviewReasons.Unreadable;
                t.SettlementVerifiedAt = nowUtc.AddMinutes(-1);
            });

        var sut = BuildSut();
        var outcome = await sut.ClearSettlementAsync(
            _admin.Id, tx.Id,
            new ClearSettlementRequest("İkinci kez kapatmayı dene"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ClearSettlementStatus.AlreadyResolved, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.SettlementAlreadyResolved, outcome.ErrorCode);
    }

    [Fact]
    public async Task ClearSettlementAsync_With_An_Active_Dispute_Is_Refused()
    {
        // A dispute owns the outcome; two admin surfaces deciding the same
        // transaction is exactly the race AD29's hold guard exists to stop.
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withBuyer: true,
            configure: t =>
            {
                t.PayoutEligibleAt = nowUtc.AddDays(-1);
                t.SettlementEscalatedAt = nowUtc.AddHours(-2);
                t.SettlementEscalationReason = SettlementReviewReasons.AmbiguousDeparture;
                t.HasActiveDispute = true;
            });

        var sut = BuildSut();
        var outcome = await sut.ClearSettlementAsync(
            _admin.Id, tx.Id,
            new ClearSettlementRequest("Dispute açıkken kapatmayı dene"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ClearSettlementStatus.InvalidStateTransition, outcome.Status);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Null(persisted.SettlementVerifiedAt);
    }

    [Fact]
    public async Task ClearSettlementAsync_Outside_ItemDelivered_Is_Refused()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.ClearSettlementAsync(
            _admin.Id, tx.Id,
            new ClearSettlementRequest("Teslimat olmadan mutabakat kapatmayı dene"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ClearSettlementStatus.InvalidStateTransition, outcome.Status);
    }

    [Fact]
    public async Task ClearSettlementAsync_With_A_Too_Short_Reason_Is_Refused()
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_DELIVERED, withBuyer: true,
            configure: t =>
            {
                t.PayoutEligibleAt = nowUtc.AddDays(-1);
                t.SettlementEscalatedAt = nowUtc.AddHours(-2);
                t.SettlementEscalationReason = SettlementReviewReasons.Unreadable;
            });

        var sut = BuildSut();
        var outcome = await sut.ClearSettlementAsync(
            _admin.Id, tx.Id,
            new ClearSettlementRequest("ok"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(ClearSettlementStatus.ValidationFailed, outcome.Status);
        Assert.Equal(AdminTransactionErrorCodes.ValidationError, outcome.ErrorCode);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Null(persisted.SettlementVerifiedAt);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  fixtures
    // ─────────────────────────────────────────────────────────────────────

    private async Task<Transaction> CreateTransactionAsync(
        TransactionStatus status, bool withBuyer, Action<Transaction>? configure = null)
    {
        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        var tx = new Transaction
        {
            Id = Guid.NewGuid(),
            Status = status,
            SellerId = _seller.Id,
            BuyerId = withBuyer ? _buyer.Id : (Guid?)null,
            BuyerRefundAddress = withBuyer ? BuyerWallet : null,
            BuyerIdentificationMethod = withBuyer
                ? BuyerIdentificationMethod.STEAM_ID
                : BuyerIdentificationMethod.OPEN_LINK,
            TargetBuyerSteamId = withBuyer ? _buyer.SteamId : null,
            InviteToken = withBuyer ? null : "T59-test-" + Guid.NewGuid().ToString("N")[..8],
            ItemAssetId = Guid.NewGuid().ToString("N")[..12],
            ItemClassId = "abc-class",
            ItemName = "AK-47 | Redline",
            StablecoinType = StablecoinType.USDT,
            Price = 100m,
            CommissionRate = 0.02m,
            CommissionAmount = 2m,
            TotalAmount = 102m,
            SellerPayoutAddress = ValidWallet,
            PaymentTimeoutMinutes = 1440,
            AcceptedAt = status >= TransactionStatus.ACCEPTED && status != TransactionStatus.FLAGGED
                ? nowUtc.AddMinutes(-30) : null,
            SellerReadyConfirmedAt = status >= TransactionStatus.SELLER_CONFIRMED && status != TransactionStatus.FLAGGED
                ? nowUtc.AddMinutes(-25) : null,
            PaymentReceivedAt = status >= TransactionStatus.PAYMENT_RECEIVED
                                 && status != TransactionStatus.FLAGGED
                                 && status != TransactionStatus.CANCELLED_SELLER
                                 && status != TransactionStatus.CANCELLED_BUYER
                                 && status != TransactionStatus.CANCELLED_TIMEOUT
                                 && status != TransactionStatus.CANCELLED_ADMIN
                ? nowUtc.AddMinutes(-20) : null,
            ItemDeliveredAt = status == TransactionStatus.ITEM_DELIVERED || status == TransactionStatus.COMPLETED
                ? nowUtc.AddMinutes(-15) : null,
            DeliveredBuyerAssetId = status == TransactionStatus.ITEM_DELIVERED || status == TransactionStatus.COMPLETED
                ? "300400500" : null,
            CompletedAt = status == TransactionStatus.COMPLETED ? nowUtc.AddMinutes(-10) : null,
            CancelledBy = status == TransactionStatus.CANCELLED_SELLER ? CancelledByType.SELLER
                : status == TransactionStatus.CANCELLED_BUYER ? CancelledByType.BUYER
                : status == TransactionStatus.CANCELLED_TIMEOUT ? CancelledByType.TIMEOUT
                : status == TransactionStatus.CANCELLED_ADMIN ? CancelledByType.ADMIN
                : (CancelledByType?)null,
            CancelReason = status >= TransactionStatus.CANCELLED_TIMEOUT
                ? "Pre-existing cancel reason for fixture" : null,
            CancelledAt = status >= TransactionStatus.CANCELLED_TIMEOUT ? nowUtc.AddMinutes(-5) : null,
            AcceptDeadline = status == TransactionStatus.CREATED ? nowUtc.AddHours(1) : null,
            PaymentDeadline = status == TransactionStatus.SELLER_CONFIRMED ? nowUtc.AddMinutes(45) : null,
            DeliveryDeadline = status == TransactionStatus.PAYMENT_RECEIVED
                ? nowUtc.AddMinutes(60) : null,
        };
        configure?.Invoke(tx);
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx;
    }
}
