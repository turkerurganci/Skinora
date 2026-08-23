using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Fraud.Domain.Entities;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Application.Lifecycle;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Timeouts;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;

namespace Skinora.Fraud.Application.Flags;

/// <inheritdoc cref="IFraudFlagService"/>
public sealed class FraudFlagService : IFraudFlagService
{
    /// <summary>
    /// Default <c>accept_timeout_minutes</c> value used when admin approves a
    /// pre-create flag and the SystemSetting row is absent. Mirrors
    /// <see cref="TransactionCreationService.DefaultAcceptTimeoutMinutes"/>
    /// so the post-approval CREATED state inherits the same fallback as a
    /// freshly-created transaction.
    /// </summary>
    public const int DefaultAcceptTimeoutMinutes =
        TransactionCreationService.DefaultAcceptTimeoutMinutes;

    /// <summary>
    /// Maximum trimmed length of an admin review note — matches the
    /// <c>AdminNote nvarchar(2000)</c> column (FraudFlagConfiguration, 06 §3.12)
    /// and the sibling <c>AdminUserSuspensionService</c> guard, so an over-long
    /// note returns a clean 400 VALIDATION_ERROR instead of a SaveChanges
    /// truncation 500 (07 §9.4–§9.5).
    /// </summary>
    public const int MaxNoteLength = 2000;

    private readonly AppDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly IOutboxService _outbox;
    private readonly ITransactionLimitsProvider _limits;
    private readonly ITimeoutFreezeService _freeze;
    private readonly IPaymentAddressAllocator _paymentAddressAllocator;
    private readonly TimeProvider _clock;
    private readonly ILogger<FraudFlagService> _logger;

    public FraudFlagService(
        AppDbContext db,
        IAuditLogger auditLogger,
        IOutboxService outbox,
        ITransactionLimitsProvider limits,
        ITimeoutFreezeService freeze,
        IPaymentAddressAllocator paymentAddressAllocator,
        TimeProvider clock,
        ILogger<FraudFlagService> logger)
    {
        _db = db;
        _auditLogger = auditLogger;
        _outbox = outbox;
        _limits = limits;
        _freeze = freeze;
        _paymentAddressAllocator = paymentAddressAllocator;
        _clock = clock;
        _logger = logger;
    }

    // ── Staging path (caller-owned SaveChanges) ──────────────────────────

    public async Task<Guid> StageAccountFlagAsync(
        Guid userId,
        FraudFlagType type,
        string details,
        Guid actorId,
        ActorType actorType,
        bool cascadeEmergencyHold,
        string? emergencyHoldReason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (cascadeEmergencyHold && string.IsNullOrWhiteSpace(emergencyHoldReason))
        {
            throw new ArgumentException(
                "emergencyHoldReason is required when cascadeEmergencyHold=true (02 §14.0).",
                nameof(emergencyHoldReason));
        }

        var flagId = Guid.NewGuid();
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        _db.Set<FraudFlag>().Add(new FraudFlag
        {
            Id = flagId,
            UserId = userId,
            TransactionId = null,
            Scope = FraudFlagScope.ACCOUNT_LEVEL,
            Type = type,
            Status = ReviewStatus.PENDING,
            Details = details,
        });

        var cascaded = false;
        if (cascadeEmergencyHold)
        {
            cascaded = await ApplyEmergencyHoldCascadeAsync(
                userId, actorId, actorType, emergencyHoldReason!, flagId, cancellationToken);
        }

        await _auditLogger.LogAsync(new AuditLogEntry(
            UserId: userId,
            ActorId: actorId,
            ActorType: actorType,
            Action: AuditAction.FRAUD_FLAG_CREATED,
            EntityType: nameof(FraudFlag),
            EntityId: flagId.ToString(),
            OldValue: null,
            NewValue: details,
            IpAddress: null), cancellationToken);

        await _outbox.PublishAsync(new FraudFlagCreatedEvent(
            EventId: Guid.NewGuid(),
            FraudFlagId: flagId,
            UserId: userId,
            TransactionId: null,
            Scope: FraudFlagScope.ACCOUNT_LEVEL,
            Type: type,
            EmergencyHoldAppliedToActiveTransactions: cascaded,
            OccurredAt: nowUtc), cancellationToken);

        return flagId;
    }

    public async Task<Guid> StageTransactionFlagAsync(
        Guid userId,
        Guid transactionId,
        FraudFlagType type,
        string details,
        Guid actorId,
        ActorType actorType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);

        var flagId = Guid.NewGuid();
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        _db.Set<FraudFlag>().Add(new FraudFlag
        {
            Id = flagId,
            UserId = userId,
            TransactionId = transactionId,
            Scope = FraudFlagScope.TRANSACTION_PRE_CREATE,
            Type = type,
            Status = ReviewStatus.PENDING,
            Details = details,
        });

        await _auditLogger.LogAsync(new AuditLogEntry(
            UserId: userId,
            ActorId: actorId,
            ActorType: actorType,
            Action: AuditAction.FRAUD_FLAG_CREATED,
            EntityType: nameof(FraudFlag),
            EntityId: flagId.ToString(),
            OldValue: null,
            NewValue: details,
            IpAddress: null), cancellationToken);

        await _outbox.PublishAsync(new FraudFlagCreatedEvent(
            EventId: Guid.NewGuid(),
            FraudFlagId: flagId,
            UserId: userId,
            TransactionId: transactionId,
            Scope: FraudFlagScope.TRANSACTION_PRE_CREATE,
            Type: type,
            EmergencyHoldAppliedToActiveTransactions: false,
            OccurredAt: nowUtc), cancellationToken);

        return flagId;
    }

    // ── Review path (own SaveChanges) ────────────────────────────────────

    public async Task<ApproveFlagOutcome> ApproveAsync(
        Guid flagId, Guid adminId, string? note, CancellationToken cancellationToken)
    {
        var flag = await _db.Set<FraudFlag>()
            .FirstOrDefaultAsync(f => f.Id == flagId, cancellationToken);
        if (flag is null)
            return new ApproveFlagOutcome.NotFound();
        if (flag.Status != ReviewStatus.PENDING)
            return new ApproveFlagOutcome.AlreadyReviewed();

        var normalizedNote = NormalizeNote(note);
        if (normalizedNote is { Length: > MaxNoteLength })
            return new ApproveFlagOutcome.ValidationFailed(
                $"note must not exceed {MaxNoteLength} characters.");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        TransactionStatus? finalTxStatus = null;

        // Set when a TRANSACTION_PRE_CREATE flag promotes its transaction
        // FLAGGED → CREATED, so the post-commit eager payment-address
        // allocation (WP4b) runs only for that path.
        Guid? promotedTransactionId = null;

        await using (var dbTx = await _db.Database.BeginTransactionAsync(cancellationToken))
        {
            if (flag.Scope == FraudFlagScope.TRANSACTION_PRE_CREATE)
            {
                var transaction = await _db.Set<Transaction>()
                    .FirstOrDefaultAsync(
                        t => t.Id == flag.TransactionId!.Value && !t.IsDeleted,
                        cancellationToken);
                if (transaction is null)
                    return new ApproveFlagOutcome.TransactionNotFlagged();
                if (transaction.Status != TransactionStatus.FLAGGED)
                    return new ApproveFlagOutcome.TransactionNotFlagged();

                var previousTxStatus = transaction.Status;
                var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
                machine.Fire(TransactionTrigger.AdminApprove);

                // WP15 — audit-trail row (06 §3.6) for the FLAGGED → CREATED
                // promotion. Admin actor; not reputation-affecting.
                TransactionHistoryRecorder.Record(
                    _db, transaction, previousTxStatus, TransactionTrigger.AdminApprove,
                    ActorType.ADMIN, adminId, nowUtc);

                var limits = await _limits.GetAsync(cancellationToken);
                transaction.AcceptDeadline = nowUtc + TimeSpan.FromMinutes(
                    limits.AcceptTimeoutMinutes ?? DefaultAcceptTimeoutMinutes);

                finalTxStatus = transaction.Status;
                promotedTransactionId = transaction.Id;
            }

            flag.Status = ReviewStatus.APPROVED;
            flag.ReviewedAt = nowUtc;
            flag.ReviewedByAdminId = adminId;
            flag.AdminNote = normalizedNote;

            await _auditLogger.LogAsync(new AuditLogEntry(
                UserId: flag.UserId,
                ActorId: adminId,
                ActorType: ActorType.ADMIN,
                Action: AuditAction.FRAUD_FLAG_APPROVED,
                EntityType: nameof(FraudFlag),
                EntityId: flag.Id.ToString(),
                OldValue: ReviewStatus.PENDING.ToString(),
                NewValue: ReviewStatus.APPROVED.ToString(),
                IpAddress: null), cancellationToken);

            await _outbox.PublishAsync(new FraudFlagApprovedEvent(
                EventId: Guid.NewGuid(),
                FraudFlagId: flag.Id,
                UserId: flag.UserId,
                TransactionId: flag.TransactionId,
                Scope: flag.Scope,
                Type: flag.Type,
                ReviewedByAdminId: adminId,
                OccurredAt: nowUtc), cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            await dbTx.CommitAsync(cancellationToken);
        }

        // ---------- Post-commit: eager payment-address allocation (WP4b) ----------
        // The FLAGGED → CREATED transition has committed, so the allocator's
        // CREATED/ACCEPTED eligibility guard now passes. Mirrors the inline
        // allocation TransactionCreationService runs for natively-CREATED
        // transactions — whose comment names this approve path the "future task
        // entry point". Best-effort/non-fatal: a sidecar outage must never turn
        // a committed approval into a 500 — EnsurePaymentAddressJob recovers any
        // transaction this inline call could not allocate.
        if (promotedTransactionId is { } promotedTxId)
            await TryAllocatePaymentAddressAsync(promotedTxId, cancellationToken);

        return new ApproveFlagOutcome.Success(new FraudFlagReviewResultDto(
            ReviewStatus: ReviewStatus.APPROVED,
            TransactionStatus: finalTxStatus,
            ReviewedAt: nowUtc));
    }

    public async Task<RejectFlagOutcome> RejectAsync(
        Guid flagId, Guid adminId, string? note, CancellationToken cancellationToken)
    {
        var flag = await _db.Set<FraudFlag>()
            .FirstOrDefaultAsync(f => f.Id == flagId, cancellationToken);
        if (flag is null)
            return new RejectFlagOutcome.NotFound();
        if (flag.Status != ReviewStatus.PENDING)
            return new RejectFlagOutcome.AlreadyReviewed();

        var normalizedNote = NormalizeNote(note);
        if (normalizedNote is { Length: > MaxNoteLength })
            return new RejectFlagOutcome.ValidationFailed(
                $"note must not exceed {MaxNoteLength} characters.");

        var nowUtc = _clock.GetUtcNow().UtcDateTime;
        TransactionStatus? finalTxStatus = null;

        await using var dbTx = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (flag.Scope == FraudFlagScope.TRANSACTION_PRE_CREATE)
        {
            var transaction = await _db.Set<Transaction>()
                .FirstOrDefaultAsync(
                    t => t.Id == flag.TransactionId!.Value && !t.IsDeleted,
                    cancellationToken);
            if (transaction is null)
                return new RejectFlagOutcome.TransactionNotFlagged();
            if (transaction.Status != TransactionStatus.FLAGGED)
                return new RejectFlagOutcome.TransactionNotFlagged();

            var previousTxStatus = transaction.Status;
            var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
            machine.Fire(TransactionTrigger.AdminReject);

            // WP15 — audit-trail row (06 §3.6) for the FLAGGED → CANCELLED_ADMIN
            // rejection. CANCELLED_ADMIN is excluded from reputation (02 §13).
            TransactionHistoryRecorder.Record(
                _db, transaction, previousTxStatus, TransactionTrigger.AdminReject,
                ActorType.ADMIN, adminId, nowUtc);

            finalTxStatus = transaction.Status;
        }

        flag.Status = ReviewStatus.REJECTED;
        flag.ReviewedAt = nowUtc;
        flag.ReviewedByAdminId = adminId;
        flag.AdminNote = normalizedNote;

        await _auditLogger.LogAsync(new AuditLogEntry(
            UserId: flag.UserId,
            ActorId: adminId,
            ActorType: ActorType.ADMIN,
            Action: AuditAction.FRAUD_FLAG_REJECTED,
            EntityType: nameof(FraudFlag),
            EntityId: flag.Id.ToString(),
            OldValue: ReviewStatus.PENDING.ToString(),
            NewValue: ReviewStatus.REJECTED.ToString(),
            IpAddress: null), cancellationToken);

        await _outbox.PublishAsync(new FraudFlagRejectedEvent(
            EventId: Guid.NewGuid(),
            FraudFlagId: flag.Id,
            UserId: flag.UserId,
            TransactionId: flag.TransactionId,
            Scope: flag.Scope,
            Type: flag.Type,
            ReviewedByAdminId: adminId,
            OccurredAt: nowUtc), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await dbTx.CommitAsync(cancellationToken);

        return new RejectFlagOutcome.Success(new FraudFlagReviewResultDto(
            ReviewStatus: ReviewStatus.REJECTED,
            TransactionStatus: finalTxStatus,
            ReviewedAt: nowUtc));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Iterates over the user's active transactions and applies
    /// <c>EMERGENCY_HOLD</c> to each one that is not already on hold or
    /// already terminal. Audit rows are written per transaction
    /// (<see cref="AuditAction.FRAUD_FLAG_AUTO_HOLD"/>) so the admin trail
    /// stays granular.
    /// </summary>
    private async Task<bool> ApplyEmergencyHoldCascadeAsync(
        Guid userId,
        Guid actorId,
        ActorType actorType,
        string reason,
        Guid flagId,
        CancellationToken cancellationToken)
    {
        // Active state set: anything not in TransactionStatusSets.Terminal.
        // REFUNDED is part of that set as of the F7-gate follow-up: this
        // predicate used to exclude only the five pre-v3.0 terminals, so the
        // cascade would freeze an already-refunded transaction — writing an
        // audit row and firing EMERGENCY_HOLD_APPLIED at both parties for a
        // transaction that had already been settled and closed. The method's
        // own summary said it skips rows that are "already terminal"; the code
        // did not know REFUNDED was one.
        // FLAGGED transactions are intentionally included — 07 §9.21
        // "Hold uygulanabilir state'ler: Tüm aktif state'ler (CREATED →
        // ITEM_DELIVERED + FLAGGED)".
        // The user can be either party (sanctions match on a wallet address
        // freezes that user's transactions regardless of whether they were
        // selling or buying — 03 §11a.3).
        var activeTxs = await _db.Set<Transaction>()
            .Where(t =>
                (t.SellerId == userId || t.BuyerId == userId)
                && !t.IsDeleted
                && !t.IsOnHold
                && !TransactionStatusSets.Terminal.Contains(t.Status))
            .ToListAsync(cancellationToken);

        if (activeTxs.Count == 0)
            return false;

        foreach (var tx in activeTxs)
        {
            // Freeze first so TimeoutRemainingSeconds is captured against the
            // active phase deadline (06 §3.5 matrix) — the state machine's
            // ApplyEmergencyHold only computes the remainder for the two phases
            // it knows about (SELLER_CONFIRMED, PAYMENT_RECEIVED), so without
            // this pre-pass the CK_Transactions_FreezeActive constraint rejects
            // the row whenever the active phase is CREATED or ACCEPTED. Pairs
            // with the T50 freeze engine that encodes the full matrix.
            await _freeze.FreezeAsync(tx, TimeoutFreezeReason.EMERGENCY_HOLD, cancellationToken);

            var machine = new TransactionStateMachine(tx, tx.RowVersion);
            machine.ApplyEmergencyHold(actorId, reason);

            await _auditLogger.LogAsync(new AuditLogEntry(
                UserId: userId,
                ActorId: actorId,
                ActorType: actorType,
                Action: AuditAction.FRAUD_FLAG_AUTO_HOLD,
                EntityType: nameof(Transaction),
                EntityId: tx.Id.ToString(),
                OldValue: null,
                NewValue: $"flagId={flagId};reason={reason}",
                IpAddress: null), cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Best-effort eager payment-address allocation for a just-approved
    /// transaction (WP4b). Runs OUTSIDE the approve DB transaction (the
    /// FLAGGED → CREATED transition must be committed before the allocator's
    /// CREATED/ACCEPTED eligibility guard passes). Mirrors
    /// <see cref="Skinora.Users.Application.Wallet"/>'s swallow pattern: rethrow
    /// cancellation, swallow everything else so a sidecar outage never turns a
    /// committed approval into a 500 — <c>EnsurePaymentAddressJob</c> recovers
    /// any transaction this call could not allocate.
    /// </summary>
    private async Task TryAllocatePaymentAddressAsync(
        Guid transactionId, CancellationToken cancellationToken)
    {
        try
        {
            var allocation = await _paymentAddressAllocator.AllocateAsync(
                transactionId, cancellationToken);
            if (allocation.Status is not PaymentAddressAllocationStatus.Created
                and not PaymentAddressAllocationStatus.AlreadyExisted)
            {
                _logger.LogWarning(
                    "Inline payment-address allocation skipped for approved transaction {TransactionId}: {Status} — {Message}. EnsurePaymentAddressJob will retry.",
                    transactionId, allocation.Status, allocation.ErrorMessage);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Inline payment-address allocation threw for approved transaction {TransactionId}. EnsurePaymentAddressJob will retry.",
                transactionId);
        }
    }

    private static string? NormalizeNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return null;
        var trimmed = note.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
