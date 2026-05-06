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
        return new AdminTransactionService(Context, _outbox, audit, scheduling, freeze, _clock);
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
        Assert.False(outcome.Body.ItemReturned);
        Assert.False(outcome.Body.PaymentRefunded);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal(TransactionStatus.CANCELLED_ADMIN, persisted.Status);
        Assert.Equal(CancelledByType.ADMIN, persisted.CancelledBy);
        Assert.Equal("Yasal talep — admin iptal", persisted.CancelReason);

        Assert.Empty(_outbox.Published.OfType<ItemRefundToSellerRequestedEvent>());
        Assert.Empty(_outbox.Published.OfType<PaymentRefundToBuyerRequestedEvent>());
        var cancelEvent = Assert.Single(_outbox.Published.OfType<TransactionCancelledEvent>());
        Assert.Equal(CancelledByType.ADMIN, cancelEvent.CancelledBy);
    }

    [Fact]
    public async Task CancelAsync_From_ItemEscrowed_Emits_ItemRefund_With_AdminCancel_Trigger()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_ESCROWED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _admin.Id, tx.Id,
            new AdminCancelTransactionRequest("İşlem yüksek risk taşıyor"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(AdminCancelTransactionStatus.Cancelled, outcome.Status);
        Assert.True(outcome.Body!.ItemReturned);
        Assert.False(outcome.Body.PaymentRefunded); // No payment yet at ITEM_ESCROWED.

        var refundEvent = Assert.Single(_outbox.Published.OfType<ItemRefundToSellerRequestedEvent>());
        Assert.Equal(ItemRefundTrigger.AdminCancel, refundEvent.Trigger);
        Assert.Equal(_seller.Id, refundEvent.SellerId);
        Assert.Empty(_outbox.Published.OfType<PaymentRefundToBuyerRequestedEvent>());
    }

    [Fact]
    public async Task CancelAsync_From_PaymentReceived_Emits_Both_Refund_Events()
    {
        var tx = await CreateTransactionAsync(TransactionStatus.PAYMENT_RECEIVED, withBuyer: true);

        var sut = BuildSut();
        var outcome = await sut.CancelAsync(
            _admin.Id, tx.Id,
            new AdminCancelTransactionRequest("Sanctions list match"),
            ipAddress: null,
            CancellationToken.None);

        Assert.Equal(AdminCancelTransactionStatus.Cancelled, outcome.Status);
        Assert.True(outcome.Body!.ItemReturned);
        Assert.True(outcome.Body.PaymentRefunded);

        Assert.Single(_outbox.Published.OfType<ItemRefundToSellerRequestedEvent>());
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
        var tx = await CreateTransactionAsync(TransactionStatus.ITEM_ESCROWED, withBuyer: true);
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
        Assert.Equal(TransactionStatus.ITEM_ESCROWED, outcome.Body.PreviousStatus);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.True(persisted.IsOnHold);
        Assert.Equal(_admin.Id, persisted.EmergencyHoldByAdminId);
        Assert.Equal("Sanctions screening eşleşmesi tespit edildi", persisted.EmergencyHoldReason);
        Assert.Equal((int)TransactionStatus.ITEM_ESCROWED, persisted.PreviousStatusBeforeHold);
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
        Assert.Null(outcome.Body.ItemReturned);
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
    public async Task ReleaseEmergencyHoldAsync_Cancel_From_PaymentReceived_Hold_Transitions_To_AdminCancel_With_Both_Refunds()
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
        Assert.True(outcome.Body.ItemReturned);
        Assert.True(outcome.Body.PaymentRefunded);

        var persisted = await Context.Set<Transaction>().AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.False(persisted.IsOnHold);
        Assert.Equal(TransactionStatus.CANCELLED_ADMIN, persisted.Status);
        Assert.Null(persisted.TimeoutRemainingSeconds);
        Assert.Equal(CancelledByType.ADMIN, persisted.CancelledBy);
        Assert.NotNull(persisted.CancelReason);
        Assert.Contains("Soruşturma sonucu iptal", persisted.CancelReason);

        Assert.Single(_outbox.Published.OfType<ItemRefundToSellerRequestedEvent>());
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
    //  fixtures
    // ─────────────────────────────────────────────────────────────────────

    private async Task<Transaction> CreateTransactionAsync(TransactionStatus status, bool withBuyer)
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
            ItemAssetId = "100200300",
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
            ItemEscrowedAt = status >= TransactionStatus.ITEM_ESCROWED && status != TransactionStatus.FLAGGED
                ? nowUtc.AddMinutes(-25) : null,
            EscrowBotAssetId = status >= TransactionStatus.ITEM_ESCROWED && status != TransactionStatus.FLAGGED
                ? "200300400" : null,
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
            PaymentDeadline = status == TransactionStatus.ITEM_ESCROWED ? nowUtc.AddMinutes(45) : null,
            TradeOfferToBuyerDeadline = status == TransactionStatus.PAYMENT_RECEIVED
                || status == TransactionStatus.TRADE_OFFER_SENT_TO_BUYER
                ? nowUtc.AddMinutes(60) : null,
        };
        Context.Set<Transaction>().Add(tx);
        await Context.SaveChangesAsync();
        return tx;
    }
}
