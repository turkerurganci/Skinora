using Microsoft.EntityFrameworkCore;
using Skinora.Fraud.Domain.Entities;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.Lifecycle;
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

    private readonly AppDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly IOutboxService _outbox;
    private readonly ITransactionLimitsProvider _limits;
    private readonly ITimeoutFreezeService _freeze;
    private readonly TimeProvider _clock;

    public FraudFlagService(
        AppDbContext db,
        IAuditLogger auditLogger,
        IOutboxService outbox,
        ITransactionLimitsProvider limits,
        ITimeoutFreezeService freeze,
        TimeProvider clock)
    {
        _db = db;
        _auditLogger = auditLogger;
        _outbox = outbox;
        _limits = limits;
        _freeze = freeze;
        _clock = clock;
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
                return new ApproveFlagOutcome.TransactionNotFlagged();
            if (transaction.Status != TransactionStatus.FLAGGED)
                return new ApproveFlagOutcome.TransactionNotFlagged();

            var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
            machine.Fire(TransactionTrigger.AdminApprove);

            var limits = await _limits.GetAsync(cancellationToken);
            transaction.AcceptDeadline = nowUtc + TimeSpan.FromMinutes(
                limits.AcceptTimeoutMinutes ?? DefaultAcceptTimeoutMinutes);

            finalTxStatus = transaction.Status;
        }

        flag.Status = ReviewStatus.APPROVED;
        flag.ReviewedAt = nowUtc;
        flag.ReviewedByAdminId = adminId;
        flag.AdminNote = NormalizeNote(note);

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

            var machine = new TransactionStateMachine(transaction, transaction.RowVersion);
            machine.Fire(TransactionTrigger.AdminReject);

            finalTxStatus = transaction.Status;
        }

        flag.Status = ReviewStatus.REJECTED;
        flag.ReviewedAt = nowUtc;
        flag.ReviewedByAdminId = adminId;
        flag.AdminNote = NormalizeNote(note);

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
        // Active state set: anything that is not COMPLETED or CANCELLED_*.
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
                && t.Status != TransactionStatus.COMPLETED
                && t.Status != TransactionStatus.CANCELLED_TIMEOUT
                && t.Status != TransactionStatus.CANCELLED_SELLER
                && t.Status != TransactionStatus.CANCELLED_BUYER
                && t.Status != TransactionStatus.CANCELLED_ADMIN)
            .ToListAsync(cancellationToken);

        if (activeTxs.Count == 0)
            return false;

        foreach (var tx in activeTxs)
        {
            // Freeze first so TimeoutRemainingSeconds is captured against the
            // active phase deadline (06 §3.5 matrix) — the state machine's
            // ApplyEmergencyHold only computes the remainder for ITEM_ESCROWED,
            // so without this pre-pass the CK_Transactions_FreezeActive
            // constraint rejects the row whenever the active phase is
            // CREATED / ACCEPTED / TRADE_OFFER_SENT_TO_SELLER /
            // PAYMENT_RECEIVED / TRADE_OFFER_SENT_TO_BUYER. Pairs with the
            // T50 freeze engine that already encodes the matrix correctly.
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

    private static string? NormalizeNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return null;
        var trimmed = note.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
