using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Exceptions;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Application.PostCancel;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Transactions.Application.Admin;

/// <summary>
/// T59 — 07 §9.20–§9.22 / 02 §7 / 03 §8.8 implementation. Each method commits
/// (state flip + freeze trio cleanup + timeout job cancel + outbox events +
/// audit rows) inside a single <see cref="DbContext.SaveChangesAsync"/> so the
/// admin action is atomic with its observable side effects (09 §13.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>State machine + freeze service composition:</b> the orchestrator drives
/// the T44 state machine for the status transition and emergency-hold flag
/// management, then delegates Hangfire job cancellation + reschedule logic to
/// the T50 <see cref="ITimeoutFreezeService"/>. Both layers respect the 06 §3.5
/// freeze invariants (CK_Transactions_FreezeActive/FreezePassive/FreezeHold_*).
/// </para>
/// <para>
/// <b>Permission split:</b> AD19 requires <c>CANCEL_TRANSACTIONS</c>; AD19b/c
/// require <c>EMERGENCY_HOLD</c>. The two are independent (02 §7 not).
/// </para>
/// <para>
/// <b>ITEM_DELIVERED rule:</b> hold may be applied at ITEM_DELIVERED but the
/// release path forbids <c>CANCEL</c> — only <c>RESUME</c> is permitted, which
/// matches AD19's "İptal edilemez" list (07 §9.20).
/// </para>
/// </remarks>
public sealed class AdminTransactionService : IAdminTransactionService
{
    /// <summary>Minimum trimmed length of <c>reason</c> per 07 §9.20 / §9.21.</summary>
    public const int MinReasonLength = 10;

    /// <summary>Minimum trimmed length of <c>note</c> per 07 §9.22 (spec says required; we apply ≥1 trim).</summary>
    public const int MinNoteLength = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly AppDbContext _db;
    private readonly IOutboxService _outbox;
    private readonly IAuditLogger _audit;
    private readonly ITimeoutSchedulingService _scheduling;
    private readonly ITimeoutFreezeService _freeze;
    private readonly IPostCancelMonitorStarter _postCancelMonitor;
    private readonly TimeProvider _clock;

    public AdminTransactionService(
        AppDbContext db,
        IOutboxService outbox,
        IAuditLogger audit,
        ITimeoutSchedulingService scheduling,
        ITimeoutFreezeService freeze,
        IPostCancelMonitorStarter postCancelMonitor,
        TimeProvider clock)
    {
        _db = db;
        _outbox = outbox;
        _audit = audit;
        _scheduling = scheduling;
        _postCancelMonitor = postCancelMonitor;
        _freeze = freeze;
        _clock = clock;
    }

    // ---------- AD19 — POST /admin/transactions/:id/cancel ----------

    public async Task<AdminCancelTransactionOutcome> CancelAsync(
        Guid adminUserId,
        Guid transactionId,
        AdminCancelTransactionRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ---------- Stage 1: load transaction ----------
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted, cancellationToken);
        if (transaction is null)
            return CancelFailure(AdminCancelTransactionStatus.NotFound,
                AdminTransactionErrorCodes.TransactionNotFound,
                "Transaction not found.");

        // ---------- Stage 2: reason validation ----------
        var trimmedReason = (request.Reason ?? string.Empty).Trim();
        if (trimmedReason.Length < MinReasonLength)
            return CancelFailure(AdminCancelTransactionStatus.ValidationFailed,
                AdminTransactionErrorCodes.ValidationError,
                $"reason must be at least {MinReasonLength} characters (07 §9.20).");

        // ---------- Stage 3: state guards (07 §9.20) ----------
        if (transaction.Status == TransactionStatus.ITEM_DELIVERED)
            return CancelFailure(AdminCancelTransactionStatus.CannotCancelAtDeliveryStage,
                AdminTransactionErrorCodes.CannotCancelAtDeliveryStage,
                "Item already delivered to buyer; cancel is forbidden after the delivery stage (07 §9.20).");

        if (transaction.IsOnHold)
            return CancelFailure(AdminCancelTransactionStatus.InvalidStateTransition,
                AdminTransactionErrorCodes.InvalidStateTransition,
                "Transaction is under emergency hold; release the hold first (use AD19c).");

        if (IsTerminalState(transaction.Status))
            return CancelFailure(AdminCancelTransactionStatus.InvalidStateTransition,
                AdminTransactionErrorCodes.InvalidStateTransition,
                $"Cannot cancel transaction in terminal state {transaction.Status}.");

        // ---------- Stage 4: state transition ----------
        var previousStatus = transaction.Status;
        var paymentWasReceived = PaymentWasReceived(previousStatus);

        var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
        try
        {
            machine.Fire(TransactionTrigger.AdminCancel, new CancellationContext(trimmedReason));
        }
        catch (DomainException ex)
        {
            return CancelFailure(AdminCancelTransactionStatus.InvalidStateTransition,
                ex.ErrorCode,
                ex.Message);
        }

        // ---------- Stage 5: side effects ----------
        var occurredAt = _clock.GetUtcNow().UtcDateTime;

        // WP15 — audit-trail row (06 §3.6). Admin-cancel is excluded from the
        // reputation formula (02 §13), so only the history row is written here —
        // no reputation/cooldown recompute.
        TransactionHistoryRecorder.Record(
            _db, transaction, previousStatus, TransactionTrigger.AdminCancel,
            ActorType.ADMIN, adminUserId, occurredAt);

        // 5a. Cancel pending Hangfire timeout / warning jobs (idempotent).
        await _scheduling.CancelTimeoutJobsAsync(transaction.Id, cancellationToken);

        // 5c. Payment refund when buyer had paid.
        if (paymentWasReceived && transaction.BuyerId is { } buyerForRefund
            && !string.IsNullOrEmpty(transaction.BuyerRefundAddress))
        {
            await _outbox.PublishAsync(
                new PaymentRefundToBuyerRequestedEvent(
                    EventId: Guid.NewGuid(),
                    TransactionId: transaction.Id,
                    BuyerId: buyerForRefund,
                    BuyerRefundAddress: transaction.BuyerRefundAddress,
                    OccurredAt: occurredAt),
                cancellationToken);
        }

        // 5d. Counter-party notification fan-out via the existing T51 consumer
        // (extended to handle CancelledByType.ADMIN — both parties notified).
        await _outbox.PublishAsync(
            new TransactionCancelledEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                CancelledBy: CancelledByType.ADMIN,
                SellerId: transaction.SellerId,
                BuyerId: transaction.BuyerId,
                ItemName: transaction.ItemName,
                CancelReason: trimmedReason,
                FromStatus: previousStatus,
                OccurredAt: occurredAt),
            cancellationToken);

        // 5d.1 T75 — post-cancel monitor start (idempotent on missing
        // PaymentAddress for CREATED-cancel without allocation).
        await _postCancelMonitor.RequestStartAsync(transaction.Id, occurredAt, cancellationToken);

        // 5e. Audit row.
        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: null,
                ActorId: adminUserId,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.TRANSACTION_CANCELLED_ADMIN,
                EntityType: nameof(Transaction),
                EntityId: transaction.Id.ToString(),
                OldValue: JsonSerializer.Serialize(new { Status = previousStatus.ToString() }, JsonOptions),
                NewValue: JsonSerializer.Serialize(new
                {
                    Status = transaction.Status.ToString(),
                    Reason = trimmedReason,
                    PaymentRefunded = paymentWasReceived,
                }, JsonOptions),
                IpAddress: ipAddress),
            cancellationToken);

        // ---------- Stage 6: atomic commit ----------
        await _db.SaveChangesAsync(cancellationToken);

        return new AdminCancelTransactionOutcome(
            Status: AdminCancelTransactionStatus.Cancelled,
            Body: new AdminCancelTransactionResponse(
                Status: transaction.Status,
                CancelledAt: transaction.CancelledAt!.Value,
                PaymentRefunded: paymentWasReceived),
            ErrorCode: null,
            ErrorMessage: null);
    }

    // ---------- AD19b — POST /admin/transactions/:id/emergency-hold ----------

    public async Task<ApplyEmergencyHoldOutcome> ApplyEmergencyHoldAsync(
        Guid adminUserId,
        Guid transactionId,
        ApplyEmergencyHoldRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ---------- Stage 1: load transaction ----------
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted, cancellationToken);
        if (transaction is null)
            return HoldFailure(ApplyEmergencyHoldStatus.NotFound,
                AdminTransactionErrorCodes.TransactionNotFound,
                "Transaction not found.");

        // ---------- Stage 2: reason validation ----------
        var trimmedReason = (request.Reason ?? string.Empty).Trim();
        if (trimmedReason.Length < MinReasonLength)
            return HoldFailure(ApplyEmergencyHoldStatus.ValidationFailed,
                AdminTransactionErrorCodes.ValidationError,
                $"reason must be at least {MinReasonLength} characters (07 §9.21).");

        // ---------- Stage 3: state guards (07 §9.21) ----------
        if (transaction.IsOnHold)
            return HoldFailure(ApplyEmergencyHoldStatus.AlreadyOnHold,
                AdminTransactionErrorCodes.AlreadyOnHold,
                "Transaction is already under emergency hold (07 §9.21).");

        if (IsTerminalState(transaction.Status))
            return HoldFailure(ApplyEmergencyHoldStatus.InvalidStateTransition,
                AdminTransactionErrorCodes.InvalidStateTransition,
                $"Cannot apply emergency hold to terminal state {transaction.Status}.");

        // ---------- Stage 4: T50 freeze pre-pass ----------
        // Runs BEFORE the state machine because T44 ApplyEmergencyHold only
        // resolves a remainder for the two phases it knows about
        // (SELLER_CONFIRMED → PaymentDeadline, PAYMENT_RECEIVED →
        // DeliveryDeadline) and leaves TimeoutRemainingSeconds NULL elsewhere
        // (T50 report Known Limitations — flagged for T59), which would trip
        // CK_Transactions_FreezeActive at SaveChangesAsync. T50 FreezeAsync
        // resolves the active-phase deadline from the 06 §3.5 matrix and
        // stamps the freeze trio (TimeoutFrozenAt + TimeoutFreezeReason +
        // TimeoutRemainingSeconds). It also cancels pending Hangfire jobs.
        // T54 cascade-hold uses the same pre-pass pattern.
        await _freeze.FreezeAsync(transaction, TimeoutFreezeReason.EMERGENCY_HOLD, cancellationToken);

        // ---------- Stage 5: domain stamp via state machine ----------
        // ApplyEmergencyHold sets IsOnHold + EmergencyHold* + PreviousStatusBeforeHold.
        // It also re-stamps TimeoutFrozenAt + TimeoutFreezeReason — same values
        // already written by FreezeAsync. The clock skew between TimeProvider
        // (FreezeAsync) and DateTime.UtcNow (state machine) is acceptable; no
        // CK constraint compares the two timestamps.
        var previousStatus = transaction.Status;

        var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
        try
        {
            machine.ApplyEmergencyHold(adminUserId, trimmedReason);
        }
        catch (DomainException ex)
        {
            // ApplyEmergencyHold can throw AlreadyOnHold (covered above) or
            // EmergencyHoldReasonRequired (covered above) — keep the catch as
            // a defensive belt against future invariant changes.
            return HoldFailure(ApplyEmergencyHoldStatus.InvalidStateTransition,
                ex.ErrorCode,
                ex.Message);
        }

        // ---------- Stage 6: side effects ----------
        var occurredAt = _clock.GetUtcNow().UtcDateTime;

        // 6a. Notification fan-out (seller + buyer when registered).
        await _outbox.PublishAsync(
            new EmergencyHoldAppliedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                SellerId: transaction.SellerId,
                BuyerId: transaction.BuyerId,
                ItemName: transaction.ItemName,
                Reason: trimmedReason,
                OccurredAt: occurredAt),
            cancellationToken);

        // 6b. Audit row.
        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: null,
                ActorId: adminUserId,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.EMERGENCY_HOLD_APPLIED,
                EntityType: nameof(Transaction),
                EntityId: transaction.Id.ToString(),
                OldValue: null,
                NewValue: JsonSerializer.Serialize(new
                {
                    Reason = trimmedReason,
                    PreviousStatus = previousStatus.ToString(),
                }, JsonOptions),
                IpAddress: ipAddress),
            cancellationToken);

        // ---------- Stage 7: atomic commit ----------
        await _db.SaveChangesAsync(cancellationToken);

        // 07 §9.21 response surfaces the projected "EMERGENCY_HOLD" status
        // string (not a real TransactionStatus enum value — overlay only).
        return new ApplyEmergencyHoldOutcome(
            Status: ApplyEmergencyHoldStatus.Applied,
            Body: new ApplyEmergencyHoldResponse(
                Status: "EMERGENCY_HOLD",
                FrozenAt: transaction.EmergencyHoldAt!.Value,
                PreviousStatus: previousStatus),
            ErrorCode: null,
            ErrorMessage: null);
    }

    // ---------- AD19c — POST /admin/transactions/:id/release-hold ----------

    public async Task<ReleaseEmergencyHoldOutcome> ReleaseEmergencyHoldAsync(
        Guid adminUserId,
        Guid transactionId,
        ReleaseEmergencyHoldRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ---------- Stage 1: load transaction ----------
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted, cancellationToken);
        if (transaction is null)
            return ReleaseFailure(ReleaseEmergencyHoldStatus.NotFound,
                AdminTransactionErrorCodes.TransactionNotFound,
                "Transaction not found.");

        // ---------- Stage 2: note validation ----------
        var trimmedNote = (request.Note ?? string.Empty).Trim();
        if (trimmedNote.Length < MinNoteLength)
            return ReleaseFailure(ReleaseEmergencyHoldStatus.ValidationFailed,
                AdminTransactionErrorCodes.ValidationError,
                "note is required (07 §9.22).");

        // ---------- Stage 3: hold guard ----------
        if (!transaction.IsOnHold)
            return ReleaseFailure(ReleaseEmergencyHoldStatus.NotOnHold,
                AdminTransactionErrorCodes.NotOnHold,
                "Transaction is not under emergency hold (07 §9.22).");

        // ---------- Stage 4: previousStatus + ITEM_DELIVERED CANCEL guard ----------
        // Status is unchanged by ApplyEmergencyHold (it's an overlay) so
        // tx.Status equals tx.PreviousStatusBeforeHold for active states. We
        // read the int-stored copy because that field is the explicit
        // contract surfaced by 06 §3.5 + 07 §9.21 response.
        var previousStatus = transaction.PreviousStatusBeforeHold.HasValue
            ? (TransactionStatus)transaction.PreviousStatusBeforeHold.Value
            : transaction.Status;

        if (request.Action == EmergencyHoldReleaseAction.CANCEL
            && previousStatus == TransactionStatus.ITEM_DELIVERED)
        {
            return ReleaseFailure(ReleaseEmergencyHoldStatus.CannotCancelDeliveredHold,
                AdminTransactionErrorCodes.CannotCancelDeliveredHold,
                "Cannot cancel a hold whose pre-hold status was ITEM_DELIVERED — only RESUME is permitted (07 §9.22).");
        }

        return request.Action switch
        {
            EmergencyHoldReleaseAction.RESUME => await ResumeAsync(
                transaction, adminUserId, trimmedNote, previousStatus, ipAddress, cancellationToken),

            EmergencyHoldReleaseAction.CANCEL => await CancelAfterHoldAsync(
                transaction, adminUserId, trimmedNote, previousStatus, ipAddress, cancellationToken),

            _ => ReleaseFailure(ReleaseEmergencyHoldStatus.ValidationFailed,
                AdminTransactionErrorCodes.ValidationError,
                $"Unknown release action '{request.Action}'."),
        };
    }

    // ---------- Internal: RESUME branch ----------

    private async Task<ReleaseEmergencyHoldOutcome> ResumeAsync(
        Transaction transaction,
        Guid adminUserId,
        string trimmedNote,
        TransactionStatus previousStatus,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Resume order matters: T50 ResumeAsync needs TimeoutFrozenAt set to
        // detect "is frozen?", reschedules Hangfire jobs (SELLER_CONFIRMED) and
        // clears the freeze trio. ReleaseEmergencyHold (state machine) then
        // flips IsOnHold off — the freeze trio fields it would clear are
        // already null after ResumeAsync, so the second clear is a no-op.
        await _freeze.ResumeAsync(transaction, cancellationToken);

        var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
        try
        {
            machine.ReleaseEmergencyHold();
        }
        catch (DomainException ex)
        {
            return ReleaseFailure(ReleaseEmergencyHoldStatus.NotOnHold,
                ex.ErrorCode,
                ex.Message);
        }

        var occurredAt = _clock.GetUtcNow().UtcDateTime;

        // Outbox event — notification fan-out.
        await _outbox.PublishAsync(
            new EmergencyHoldReleasedEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                SellerId: transaction.SellerId,
                BuyerId: transaction.BuyerId,
                ItemName: transaction.ItemName,
                Action: EmergencyHoldReleaseAction.RESUME,
                ResumedStatus: previousStatus,
                OccurredAt: occurredAt),
            cancellationToken);

        // Audit row.
        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: null,
                ActorId: adminUserId,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.EMERGENCY_HOLD_RELEASED,
                EntityType: nameof(Transaction),
                EntityId: transaction.Id.ToString(),
                OldValue: JsonSerializer.Serialize(new { IsOnHold = true }, JsonOptions),
                NewValue: JsonSerializer.Serialize(new
                {
                    Action = nameof(EmergencyHoldReleaseAction.RESUME),
                    Note = trimmedNote,
                    ResumedStatus = previousStatus.ToString(),
                }, JsonOptions),
                IpAddress: ipAddress),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new ReleaseEmergencyHoldOutcome(
            Status: ReleaseEmergencyHoldStatus.Released,
            Body: new ReleaseEmergencyHoldResponse(
                Status: previousStatus,
                ReleasedAt: occurredAt,
                Action: EmergencyHoldReleaseAction.RESUME,
                PaymentRefunded: null),
            ErrorCode: null,
            ErrorMessage: null);
    }

    // ---------- Internal: CANCEL branch (release + admin cancel) ----------

    private async Task<ReleaseEmergencyHoldOutcome> CancelAfterHoldAsync(
        Transaction transaction,
        Guid adminUserId,
        string trimmedNote,
        TransactionStatus previousStatus,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var paymentWasReceived = PaymentWasReceived(previousStatus);

        // Step 1 — release the hold so the state machine permits AdminCancel
        // (Fire enforces NOT IsOnHold per 05 §4.5).
        var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
        try
        {
            machine.ReleaseEmergencyHold();
        }
        catch (DomainException ex)
        {
            return ReleaseFailure(ReleaseEmergencyHoldStatus.NotOnHold,
                ex.ErrorCode,
                ex.Message);
        }

        // Step 2 — clear TimeoutRemainingSeconds so CK_Transactions_FreezePassive
        // holds when SaveChangesAsync runs (state machine ReleaseEmergencyHold
        // clears TimeoutFrozenAt + TimeoutFreezeReason but preserves
        // TimeoutRemainingSeconds for T47 reschedule, which is moot here — we
        // are about to cancel the transaction).
        transaction.TimeoutRemainingSeconds = null;

        // Step 3 — fire AdminCancel. Hangfire jobs were already cancelled at
        // hold-time by FreezeAsync; CancelTimeoutJobsAsync below stays
        // defensive (idempotent).
        var cancelReason = $"Hold sonrası iptal: {trimmedNote}";
        try
        {
            machine.Fire(TransactionTrigger.AdminCancel, new CancellationContext(cancelReason));
        }
        catch (DomainException ex)
        {
            return ReleaseFailure(ReleaseEmergencyHoldStatus.ValidationFailed,
                ex.ErrorCode,
                ex.Message);
        }

        await _scheduling.CancelTimeoutJobsAsync(transaction.Id, cancellationToken);

        var occurredAt = _clock.GetUtcNow().UtcDateTime;

        // WP15 — audit-trail row (06 §3.6) for the hold-release-then-cancel path.
        // CANCELLED_ADMIN is excluded from reputation (02 §13) — history only.
        TransactionHistoryRecorder.Record(
            _db, transaction, previousStatus, TransactionTrigger.AdminCancel,
            ActorType.ADMIN, adminUserId, occurredAt);

        // Step 4 — refund + notification + audit fan-out (same as AD19).
        // No item-return branch: the platform never holds the item (02 §9).
        if (paymentWasReceived && transaction.BuyerId is { } buyerForRefund
            && !string.IsNullOrEmpty(transaction.BuyerRefundAddress))
        {
            await _outbox.PublishAsync(
                new PaymentRefundToBuyerRequestedEvent(
                    EventId: Guid.NewGuid(),
                    TransactionId: transaction.Id,
                    BuyerId: buyerForRefund,
                    BuyerRefundAddress: transaction.BuyerRefundAddress,
                    OccurredAt: occurredAt),
                cancellationToken);
        }

        await _outbox.PublishAsync(
            new TransactionCancelledEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transaction.Id,
                CancelledBy: CancelledByType.ADMIN,
                SellerId: transaction.SellerId,
                BuyerId: transaction.BuyerId,
                ItemName: transaction.ItemName,
                CancelReason: cancelReason,
                FromStatus: previousStatus,
                OccurredAt: occurredAt),
            cancellationToken);

        // T75 — emergency-hold release with CANCEL action also opens the
        // post-cancel monitor window. Same idempotency contract as AD19.
        await _postCancelMonitor.RequestStartAsync(transaction.Id, occurredAt, cancellationToken);

        // Two audit rows — release + cancel — keep the forensic trail explicit.
        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: null,
                ActorId: adminUserId,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.EMERGENCY_HOLD_RELEASED,
                EntityType: nameof(Transaction),
                EntityId: transaction.Id.ToString(),
                OldValue: JsonSerializer.Serialize(new { IsOnHold = true }, JsonOptions),
                NewValue: JsonSerializer.Serialize(new
                {
                    Action = nameof(EmergencyHoldReleaseAction.CANCEL),
                    Note = trimmedNote,
                }, JsonOptions),
                IpAddress: ipAddress),
            cancellationToken);

        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: null,
                ActorId: adminUserId,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.TRANSACTION_CANCELLED_ADMIN,
                EntityType: nameof(Transaction),
                EntityId: transaction.Id.ToString(),
                OldValue: JsonSerializer.Serialize(new { Status = previousStatus.ToString() }, JsonOptions),
                NewValue: JsonSerializer.Serialize(new
                {
                    Status = transaction.Status.ToString(),
                    Reason = cancelReason,
                    PaymentRefunded = paymentWasReceived,
                }, JsonOptions),
                IpAddress: ipAddress),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new ReleaseEmergencyHoldOutcome(
            Status: ReleaseEmergencyHoldStatus.Released,
            Body: new ReleaseEmergencyHoldResponse(
                Status: transaction.Status,
                ReleasedAt: transaction.CancelledAt!.Value,
                Action: EmergencyHoldReleaseAction.CANCEL,
                PaymentRefunded: paymentWasReceived),
            ErrorCode: null,
            ErrorMessage: null);
    }

    // ---------- AD19d — POST /admin/transactions/hold-by-user/:userId ----------

    public async Task<HoldUserTransactionsOutcome> HoldAllUserTransactionsAsync(
        Guid adminUserId,
        Guid targetUserId,
        HoldUserTransactionsRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ---------- Stage 1: reason validation ----------
        var trimmedReason = (request.Reason ?? string.Empty).Trim();
        if (trimmedReason.Length < MinReasonLength)
            return new HoldUserTransactionsOutcome(
                HoldUserTransactionsStatus.ValidationFailed,
                Body: null,
                ErrorCode: AdminTransactionErrorCodes.ValidationError,
                ErrorMessage: $"reason must be at least {MinReasonLength} characters (03 §8.8).");

        // ---------- Stage 2: load the user's active transactions ----------
        // Active = not deleted, not already on hold, not terminal. The user may
        // be on either side of the trade — an account/sanctions flag freezes
        // that user's transactions regardless of side (03 §11a.3). Mirrors the
        // T54 FraudFlagService.ApplyEmergencyHoldCascadeAsync selection. The
        // !IsOnHold filter makes the call idempotent (a re-run holds 0).
        var activeTxs = await _db.Set<Transaction>()
            .Where(t =>
                (t.SellerId == targetUserId || t.BuyerId == targetUserId)
                && !t.IsDeleted
                && !t.IsOnHold
                && !TransactionStatusSets.Terminal.Contains(t.Status))
            .ToListAsync(cancellationToken);

        var occurredAt = _clock.GetUtcNow().UtcDateTime;
        var heldIds = new List<Guid>(activeTxs.Count);

        // ---------- Stage 3: per-transaction hold (same sequence as AD19b) ----------
        foreach (var transaction in activeTxs)
        {
            var previousStatus = transaction.Status;

            // Freeze pre-pass before the state machine — resolves the active-phase
            // deadline from the 06 §3.5 matrix and stamps the freeze trio so
            // CK_Transactions_FreezeActive holds for non-SELLER_CONFIRMED states; it
            // also cancels the pending Hangfire jobs (T50).
            await _freeze.FreezeAsync(transaction, TimeoutFreezeReason.EMERGENCY_HOLD, cancellationToken);

            var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
            machine.ApplyEmergencyHold(adminUserId, trimmedReason);

            // Per-transaction notification fan-out (seller + buyer when registered)
            // — same EmergencyHoldAppliedEvent as AD19b so each party is told why
            // their transaction was frozen (03 §8.8).
            await _outbox.PublishAsync(
                new EmergencyHoldAppliedEvent(
                    EventId: Guid.NewGuid(),
                    TransactionId: transaction.Id,
                    SellerId: transaction.SellerId,
                    BuyerId: transaction.BuyerId,
                    ItemName: transaction.ItemName,
                    Reason: trimmedReason,
                    OccurredAt: occurredAt),
                cancellationToken);

            await _audit.LogAsync(
                new AuditLogEntry(
                    UserId: targetUserId,
                    ActorId: adminUserId,
                    ActorType: ActorType.ADMIN,
                    Action: AuditAction.EMERGENCY_HOLD_APPLIED,
                    EntityType: nameof(Transaction),
                    EntityId: transaction.Id.ToString(),
                    OldValue: null,
                    NewValue: JsonSerializer.Serialize(new
                    {
                        Reason = trimmedReason,
                        PreviousStatus = previousStatus.ToString(),
                        BulkUserHold = true,
                    }, JsonOptions),
                    IpAddress: ipAddress),
                cancellationToken);

            heldIds.Add(transaction.Id);
        }

        // ---------- Stage 4: atomic commit ----------
        // No active transactions → heldIds empty, SaveChanges flushes nothing:
        // a safe idempotent no-op (HeldCount = 0).
        await _db.SaveChangesAsync(cancellationToken);

        return new HoldUserTransactionsOutcome(
            Status: HoldUserTransactionsStatus.Applied,
            Body: new HoldUserTransactionsResponse(
                HeldCount: heldIds.Count,
                AppliedAt: occurredAt,
                HeldTransactionIds: heldIds),
            ErrorCode: null,
            ErrorMessage: null);
    }

    // ---------- AD32 — POST /admin/transactions/:id/clear-settlement ----------

    public async Task<ClearSettlementOutcome> ClearSettlementAsync(
        Guid adminUserId,
        Guid transactionId,
        ClearSettlementRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ---------- Stage 1: load transaction ----------
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(t => t.Id == transactionId && !t.IsDeleted, cancellationToken);
        if (transaction is null)
            return ClearSettlementFailure(ClearSettlementStatus.NotFound,
                AdminTransactionErrorCodes.TransactionNotFound,
                "Transaction not found.");

        // ---------- Stage 2: reason validation ----------
        // Same floor as AD19: this decision releases money the automated check
        // refused to release, so the audit row has to say why in a sentence.
        var trimmedReason = (request.Reason ?? string.Empty).Trim();
        if (trimmedReason.Length < MinReasonLength)
            return ClearSettlementFailure(ClearSettlementStatus.ValidationFailed,
                AdminTransactionErrorCodes.ValidationError,
                $"reason must be at least {MinReasonLength} characters (07 §9.22b).");

        // ---------- Stage 3: state guards (07 §9.22b) ----------
        if (transaction.Status != TransactionStatus.ITEM_DELIVERED)
            return ClearSettlementFailure(ClearSettlementStatus.InvalidStateTransition,
                AdminTransactionErrorCodes.InvalidStateTransition,
                $"Settlement can only be cleared while the transaction is ITEM_DELIVERED (was {transaction.Status}).");

        if (transaction.IsOnHold)
            return ClearSettlementFailure(ClearSettlementStatus.InvalidStateTransition,
                AdminTransactionErrorCodes.InvalidStateTransition,
                "Transaction is under emergency hold; release the hold first (use AD19c).");

        if (transaction.HasActiveDispute)
            return ClearSettlementFailure(ClearSettlementStatus.InvalidStateTransition,
                AdminTransactionErrorCodes.InvalidStateTransition,
                "An active dispute owns this transaction; resolve it through AD29 instead.");

        // Already decided, in either direction — nothing left to close.
        if (transaction.SettlementVerifiedAt is not null || transaction.DeliveryReversedAt is not null)
            return ClearSettlementFailure(ClearSettlementStatus.AlreadyResolved,
                AdminTransactionErrorCodes.SettlementAlreadyResolved,
                "Settlement has already been resolved for this transaction.");

        // The admin closes what the platform asked about. Without this guard the
        // endpoint would be a way to pay a seller before the reversal window it
        // exists to enforce has even elapsed (02 §4.5.1).
        if (transaction.SettlementEscalatedAt is null)
            return ClearSettlementFailure(ClearSettlementStatus.NotEscalated,
                AdminTransactionErrorCodes.SettlementNotEscalated,
                "Settlement has not been escalated for review; only an escalated settlement can be cleared.");

        // ---------- Stage 4: clearance ----------
        // No state machine trigger and no status change. COMPLETED must still
        // arrive the ordinary way — payout first, PayoutCompletedConsumer after
        // — so what an admin decision does here is exactly what a successful
        // check does: open the gate the three money paths read.
        var occurredAt = _clock.GetUtcNow().UtcDateTime;
        var escalationReason = transaction.SettlementEscalationReason ?? "UNKNOWN";

        transaction.SettlementVerifiedAt = occurredAt;
        transaction.SettlementClearedByAdminId = adminUserId;

        // ---------- Stage 5: side effects ----------
        // WP15 — audit-trail row (06 §3.6). The string overload is used on
        // purpose: this is not a state transition, so there is no
        // TransactionTrigger to name, exactly as with the genesis row.
        TransactionHistoryRecorder.Record(
            _db, transaction, transaction.Status, ClearSettlementTrigger,
            ActorType.ADMIN, adminUserId, occurredAt,
            additionalData: JsonSerializer.Serialize(new
            {
                EscalationReason = escalationReason,
                Reason = trimmedReason,
            }, JsonOptions));

        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: null,
                ActorId: adminUserId,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.SETTLEMENT_CLEARED_ADMIN,
                EntityType: nameof(Transaction),
                EntityId: transaction.Id.ToString(),
                OldValue: JsonSerializer.Serialize(new
                {
                    SettlementVerifiedAt = (DateTime?)null,
                    EscalationReason = escalationReason,
                    EscalatedAt = transaction.SettlementEscalatedAt,
                }, JsonOptions),
                NewValue: JsonSerializer.Serialize(new
                {
                    transaction.SettlementVerifiedAt,
                    ClearedBy = adminUserId,
                    Reason = trimmedReason,
                }, JsonOptions),
                IpAddress: ipAddress),
            cancellationToken);

        // ---------- Stage 6: atomic commit ----------
        await _db.SaveChangesAsync(cancellationToken);

        return new ClearSettlementOutcome(
            Status: ClearSettlementStatus.Cleared,
            Body: new ClearSettlementResponse(
                Status: transaction.Status,
                SettlementVerifiedAt: occurredAt,
                EscalationReason: escalationReason),
            ErrorCode: null,
            ErrorMessage: null);
    }

    // ---------- helpers ----------

    /// <summary>
    /// Trigger label for the AD32 history row. A string rather than a
    /// <c>TransactionTrigger</c> because no transition happens: modelling it as
    /// a reentrant ITEM_DELIVERED transition would re-run that state's
    /// <c>OnEntry</c> and overwrite <c>ItemDeliveredAt</c> with the time of the
    /// admin's click.
    /// </summary>
    public const string ClearSettlementTrigger = "AdminClearSettlement";

    private static bool IsTerminalState(TransactionStatus status) => status switch
    {
        TransactionStatus.COMPLETED => true,
        TransactionStatus.CANCELLED_TIMEOUT => true,
        TransactionStatus.CANCELLED_SELLER => true,
        TransactionStatus.CANCELLED_BUYER => true,
        TransactionStatus.CANCELLED_ADMIN => true,
        TransactionStatus.REFUNDED => true,
        _ => false,
    };

    // ItemWasOnPlatform removed in v3.0: the platform never holds the item, so
    // no admin action can trigger an item return (02 §9).
    private static bool PaymentWasReceived(TransactionStatus status) => status switch
    {
        TransactionStatus.PAYMENT_RECEIVED => true,
        TransactionStatus.ITEM_DELIVERED => true,
        _ => false,
    };

    private static AdminCancelTransactionOutcome CancelFailure(
        AdminCancelTransactionStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);

    private static ApplyEmergencyHoldOutcome HoldFailure(
        ApplyEmergencyHoldStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);

    private static ReleaseEmergencyHoldOutcome ReleaseFailure(
        ReleaseEmergencyHoldStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);

    private static ClearSettlementOutcome ClearSettlementFailure(
        ClearSettlementStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);
}
