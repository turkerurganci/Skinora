using Microsoft.EntityFrameworkCore;
using Skinora.Disputes.Application.AutoCheckers;
using Skinora.Disputes.Domain.Entities;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;
using Skinora.Users.Domain.Entities;

namespace Skinora.Disputes.Application.Disputes;

/// <summary>
/// T58 — 02 §10, 03 §6, 07 §7.8–§7.10 implementation. All side effects
/// (Dispute row insert/update, <see cref="Transaction.HasActiveDispute"/>
/// toggle, outbox event emit) land inside a single
/// <see cref="DbContext.SaveChangesAsync"/> so the dispute lifecycle is
/// atomic with the emitted events (09 §13.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Per-type allowed states:</b> the canDispute envelope on
/// <see cref="Skinora.Transactions.Application.Lifecycle.TransactionDetailService"/>
/// computes a union of states across types, but each dispute type has its
/// own semantically meaningful subset enforced here:
/// </para>
/// <list type="bullet">
///   <item>PAYMENT — ITEM_ESCROWED, PAYMENT_RECEIVED.</item>
///   <item>DELIVERY — TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED.</item>
///   <item>WRONG_ITEM — ITEM_DELIVERED.</item>
/// </list>
/// <para>
/// <b>Duplicate type rule (02 §10.2):</b> a dispute of the same type cannot be
/// reopened for a transaction even after closure. The
/// <c>UQ_Disputes_TransactionId_Type</c> unique index is unfiltered (includes
/// soft-deleted rows) and enforces this at the DB level. The service applies
/// a defensive pre-check via <c>IgnoreQueryFilters</c> so the API surfaces
/// <c>DUPLICATE_DISPUTE</c> instead of a generic conflict.
/// </para>
/// <para>
/// <b>HasActiveDispute toggle:</b> set when an OPEN/ESCALATED dispute is
/// persisted; cleared by submit-txhash auto-resolve when no other
/// non-CLOSED dispute remains. Manual escalate keeps the flag set
/// (ESCALATED is still active per 06 §3.11).
/// </para>
/// <para>
/// <b>Auto-escalation:</b> only the WRONG_ITEM checker can short-circuit a
/// just-opened dispute to ESCALATED (03 §6.3, Sonuç B). The dispute row is
/// persisted with status=ESCALATED on insert; both parties are notified via
/// <see cref="DisputeEscalatedEvent"/> with <c>AutoEscalated=true</c>.
/// </para>
/// <para>
/// <b>ACTIVE_DISPUTE_EXISTS:</b> 03 §6 explicitly allows concurrent disputes
/// of different types, so this code is unreachable by design. WP5 removed it
/// from the 07 §7.8 Hatalar list — only the same-type repeat is blocked
/// (DUPLICATE_DISPUTE). No code path emits it.
/// </para>
/// </remarks>
public sealed class DisputeService : IDisputeService
{
    public const int MinEscalateDetailLength = 10;
    public const int MinTxHashLength = 16;

    private readonly AppDbContext _db;
    private readonly IOutboxService _outbox;
    private readonly IPaymentDisputeAutoChecker _paymentChecker;
    private readonly IDeliveryDisputeAutoChecker _deliveryChecker;
    private readonly IWrongItemDisputeAutoChecker _wrongItemChecker;
    private readonly TimeProvider _clock;

    public DisputeService(
        AppDbContext db,
        IOutboxService outbox,
        IPaymentDisputeAutoChecker paymentChecker,
        IDeliveryDisputeAutoChecker deliveryChecker,
        IWrongItemDisputeAutoChecker wrongItemChecker,
        TimeProvider clock)
    {
        _db = db;
        _outbox = outbox;
        _paymentChecker = paymentChecker;
        _deliveryChecker = deliveryChecker;
        _wrongItemChecker = wrongItemChecker;
        _clock = clock;
    }

    public async Task<OpenDisputeOutcome> OpenAsync(
        Guid callerUserId,
        Guid transactionId,
        OpenDisputeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Stage 1 — load transaction (tracked; we may flip HasActiveDispute).
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(
                t => t.Id == transactionId && !t.IsDeleted, cancellationToken);
        if (transaction is null)
            return OpenFailure(OpenDisputeStatus.NotFound,
                DisputeErrorCodes.TransactionNotFound, "Transaction not found.");

        // Stage 2 — buyer guard (only the buyer can open per 02 §10.2).
        if (transaction.BuyerId is null || transaction.BuyerId.Value != callerUserId)
            return OpenFailure(OpenDisputeStatus.NotBuyer,
                DisputeErrorCodes.NotBuyer,
                "Only the buyer can open a dispute on this transaction.");

        // Stage 3 — per-type state guard (shared canonical matrix).
        if (!DisputeEligibility.AllowedStatesByType.TryGetValue(request.Type, out var allowedStates)
            || !allowedStates.Contains(transaction.Status))
        {
            return OpenFailure(OpenDisputeStatus.InvalidStateTransition,
                DisputeErrorCodes.InvalidStateTransition,
                $"Dispute type {request.Type} cannot be opened in state {transaction.Status} (02 §10).");
        }

        // Stage 4 — duplicate-type guard (UQ_Disputes_TransactionId_Type).
        // IgnoreQueryFilters so soft-deleted disputes still block reopening
        // per 02 §10.2.
        var duplicate = await _db.Set<Dispute>()
            .IgnoreQueryFilters()
            .AnyAsync(
                d => d.TransactionId == transactionId && d.Type == request.Type,
                cancellationToken);
        if (duplicate)
            return OpenFailure(OpenDisputeStatus.DuplicateDispute,
                DisputeErrorCodes.DuplicateDispute,
                $"A {request.Type} dispute already exists for this transaction (02 §10.2).");

        // Stage 5 — run the type-specific auto-checker.
        var autoCheck = await RunAutoCheckerAsync(request.Type, transaction, cancellationToken);

        // WP17 — render the auto-checker result key in the disputing buyer's
        // locale. The buyer is the sole recipient of the open response, the
        // stored SystemCheckResult and the DISPUTE_RESULT notification, so a
        // single produce-time localization keeps all three in one language.
        var buyerLocale = await ResolveLocaleAsync(callerUserId, cancellationToken);
        var autoCheckText = DisputeAutoCheckMessages.Localize(autoCheck.MessageKey, buyerLocale);

        // Stage 6 — build the dispute row + decide initial status.
        var now = _clock.GetUtcNow().UtcDateTime;
        var status = autoCheck.Resolved
            ? DisputeStatus.CLOSED
            : autoCheck.AutoEscalated
                ? DisputeStatus.ESCALATED
                : DisputeStatus.OPEN;

        var dispute = new Dispute
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            OpenedByUserId = callerUserId,
            Type = request.Type,
            Status = status,
            SystemCheckResult = autoCheckText,
            ResolvedAt = autoCheck.Resolved ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Set<Dispute>().Add(dispute);

        // Stage 7 — Transaction.HasActiveDispute toggle.
        // ESCALATED + OPEN both count as active; CLOSED does not.
        var becomesActive = !autoCheck.Resolved;
        if (becomesActive && !transaction.HasActiveDispute)
            transaction.HasActiveDispute = true;

        // Stage 8 — outbox events.
        if (autoCheck.Resolved)
        {
            await _outbox.PublishAsync(
                new DisputeAutoResolvedEvent(
                    EventId: Guid.NewGuid(),
                    DisputeId: dispute.Id,
                    TransactionId: transactionId,
                    Type: dispute.Type,
                    BuyerId: callerUserId,
                    Outcome: autoCheckText,
                    OccurredAt: now),
                cancellationToken);
        }
        else if (autoCheck.AutoEscalated)
        {
            await _outbox.PublishAsync(
                new DisputeEscalatedEvent(
                    EventId: Guid.NewGuid(),
                    DisputeId: dispute.Id,
                    TransactionId: transactionId,
                    Type: dispute.Type,
                    SellerId: transaction.SellerId,
                    BuyerId: callerUserId,
                    AutoEscalated: true,
                    Detail: null,
                    OccurredAt: now),
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Stage 9 — assemble response per 07 §7.8.
        var body = new OpenDisputeResponse(
            Id: dispute.Id,
            Type: dispute.Type,
            Status: dispute.Status,
            AutoCheckResult: new AutoCheckResultDto(
                Resolved: autoCheck.Resolved,
                Message: autoCheckText,
                CanSubmitTxHash: autoCheck.CanSubmitTxHash,
                CanEscalate: autoCheck.CanEscalate),
            CreatedAt: now);

        return new OpenDisputeOutcome(
            Status: OpenDisputeStatus.Opened,
            Body: body,
            ErrorCode: null,
            ErrorMessage: null);
    }

    public async Task<SubmitTxHashOutcome> SubmitTxHashAsync(
        Guid callerUserId,
        Guid transactionId,
        Guid disputeId,
        SubmitTxHashRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Stage 1 — load dispute (tracked, scoped to this transaction).
        var dispute = await _db.Set<Dispute>()
            .FirstOrDefaultAsync(
                d => d.Id == disputeId && d.TransactionId == transactionId,
                cancellationToken);
        if (dispute is null)
            return SubmitTxHashFailure(SubmitTxHashStatus.NotFound,
                DisputeErrorCodes.DisputeNotFound, "Dispute not found.");

        // Stage 2 — type guard.
        if (dispute.Type != DisputeType.PAYMENT)
            return SubmitTxHashFailure(SubmitTxHashStatus.NotPaymentDispute,
                DisputeErrorCodes.NotPaymentDispute,
                "submit-txhash is only valid for PAYMENT disputes (07 §7.9).");

        // Stage 3 — status guard.
        if (dispute.Status != DisputeStatus.OPEN)
            return SubmitTxHashFailure(SubmitTxHashStatus.DisputeClosed,
                DisputeErrorCodes.DisputeClosed,
                "Dispute is no longer open (07 §7.9).");

        // Stage 4 — load transaction + buyer guard.
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(
                t => t.Id == transactionId && !t.IsDeleted, cancellationToken);
        if (transaction is null)
            return SubmitTxHashFailure(SubmitTxHashStatus.NotFound,
                DisputeErrorCodes.TransactionNotFound, "Transaction not found.");

        if (transaction.BuyerId is null || transaction.BuyerId.Value != callerUserId)
            return SubmitTxHashFailure(SubmitTxHashStatus.NotBuyer,
                DisputeErrorCodes.NotBuyer,
                "Only the buyer can submit a tx hash for this dispute.");

        // Stage 5 — basic hash validation. Tron hex hashes are 64 chars, but
        // we keep the floor at 16 to accept pre-validated short forms in
        // tests; full format validation is the sidecar's responsibility (T71).
        var trimmedHash = (request.TxHash ?? string.Empty).Trim();
        if (trimmedHash.Length < MinTxHashLength)
            return SubmitTxHashFailure(SubmitTxHashStatus.ValidationFailed,
                DisputeErrorCodes.ValidationError,
                $"txHash must be at least {MinTxHashLength} characters (07 §7.9).");

        // Stage 6 — re-run the auto-check with the supplied hash.
        var autoCheck = await _paymentChecker.CheckWithTxHashAsync(
            transaction, trimmedHash, cancellationToken);

        // WP17 — localize the result key in the buyer's locale (see OpenAsync).
        var buyerLocale = await ResolveLocaleAsync(callerUserId, cancellationToken);
        var autoCheckText = DisputeAutoCheckMessages.Localize(autoCheck.MessageKey, buyerLocale);

        var now = _clock.GetUtcNow().UtcDateTime;
        dispute.SystemCheckResult = autoCheckText;
        dispute.UpdatedAt = now;

        if (autoCheck.Resolved)
        {
            dispute.Status = DisputeStatus.CLOSED;
            dispute.ResolvedAt = now;

            // Stage 7 — clear HasActiveDispute when no other non-CLOSED
            // dispute remains for this transaction.
            await UpdateActiveDisputeFlagAsync(transaction, dispute.Id, cancellationToken);

            await _outbox.PublishAsync(
                new DisputeAutoResolvedEvent(
                    EventId: Guid.NewGuid(),
                    DisputeId: dispute.Id,
                    TransactionId: transactionId,
                    Type: dispute.Type,
                    BuyerId: callerUserId,
                    Outcome: autoCheckText,
                    OccurredAt: now),
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var body = new SubmitTxHashResponse(
            CheckResult: new TxHashCheckResultDto(
                Resolved: autoCheck.Resolved,
                Message: autoCheckText));

        return new SubmitTxHashOutcome(
            Status: SubmitTxHashStatus.Processed,
            Body: body,
            ErrorCode: null,
            ErrorMessage: null);
    }

    public async Task<EscalateDisputeOutcome> EscalateAsync(
        Guid callerUserId,
        Guid transactionId,
        Guid disputeId,
        EscalateDisputeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Stage 1 — load dispute.
        var dispute = await _db.Set<Dispute>()
            .FirstOrDefaultAsync(
                d => d.Id == disputeId && d.TransactionId == transactionId,
                cancellationToken);
        if (dispute is null)
            return EscalateFailure(EscalateDisputeStatus.NotFound,
                DisputeErrorCodes.DisputeNotFound, "Dispute not found.");

        // Stage 2 — status guards (07 §7.10 hatalar order).
        if (dispute.Status == DisputeStatus.ESCALATED)
            return EscalateFailure(EscalateDisputeStatus.AlreadyEscalated,
                DisputeErrorCodes.AlreadyEscalated,
                "Dispute is already escalated (07 §7.10).");
        if (dispute.Status == DisputeStatus.CLOSED)
            return EscalateFailure(EscalateDisputeStatus.DisputeClosed,
                DisputeErrorCodes.DisputeClosed,
                "Dispute is closed (07 §7.10).");

        // Stage 3 — load transaction + buyer guard.
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(
                t => t.Id == transactionId && !t.IsDeleted, cancellationToken);
        if (transaction is null)
            return EscalateFailure(EscalateDisputeStatus.NotFound,
                DisputeErrorCodes.TransactionNotFound, "Transaction not found.");

        if (transaction.BuyerId is null || transaction.BuyerId.Value != callerUserId)
            return EscalateFailure(EscalateDisputeStatus.NotBuyer,
                DisputeErrorCodes.NotBuyer,
                "Only the buyer can escalate this dispute.");

        // Stage 4 — detail validation (≥10 chars trimmed per 07 §7.10).
        var trimmedDetail = (request.Detail ?? string.Empty).Trim();
        if (trimmedDetail.Length < MinEscalateDetailLength)
            return EscalateFailure(EscalateDisputeStatus.ValidationFailed,
                DisputeErrorCodes.ValidationError,
                $"detail must be at least {MinEscalateDetailLength} characters (07 §7.10).");

        // Stage 5 — promote to ESCALATED.
        var now = _clock.GetUtcNow().UtcDateTime;
        dispute.Status = DisputeStatus.ESCALATED;
        dispute.UserDescription = trimmedDetail;
        dispute.UpdatedAt = now;

        // HasActiveDispute remains true — ESCALATED is still active.
        if (!transaction.HasActiveDispute)
            transaction.HasActiveDispute = true;

        // WP17 — localize the manual-escalate outcome in the buyer's locale once.
        // The buyer is the sole recipient of BOTH the notification and the API
        // response, so the pre-localized text flows to both (matching the
        // auto-resolved single-recipient pattern); it rides on the event as
        // OutcomeText so the DISPUTE_RESULT notification body is localized too.
        var buyerLocale = await ResolveLocaleAsync(callerUserId, cancellationToken);
        var escalateMessage = DisputeAutoCheckMessages.Localize(
            DisputeAutoCheckMessages.ManualEscalated, buyerLocale);

        // Stage 6 — outbox event (manual escalation, single buyer notification).
        await _outbox.PublishAsync(
            new DisputeEscalatedEvent(
                EventId: Guid.NewGuid(),
                DisputeId: dispute.Id,
                TransactionId: transactionId,
                Type: dispute.Type,
                SellerId: transaction.SellerId,
                BuyerId: callerUserId,
                AutoEscalated: false,
                Detail: trimmedDetail,
                OccurredAt: now,
                OutcomeText: escalateMessage),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var body = new EscalateDisputeResponse(
            Status: dispute.Status,
            EscalatedAt: now,
            Message: escalateMessage);

        return new EscalateDisputeOutcome(
            Status: EscalateDisputeStatus.Escalated,
            Body: body,
            ErrorCode: null,
            ErrorMessage: null);
    }

    /// <summary>
    /// Resolve a user's stored UI locale (06 §3.1 <c>PreferredLanguage</c>),
    /// defaulting to English when unset — same source the notification
    /// dispatcher uses, so the dispute result and the DISPUTE_RESULT
    /// notification land in one language (WP17).
    /// </summary>
    private async Task<string> ResolveLocaleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var locale = await _db.Set<User>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.PreferredLanguage)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(locale) ? "en" : locale;
    }

    private Task<AutoCheckResult> RunAutoCheckerAsync(
        DisputeType type,
        Transaction transaction,
        CancellationToken cancellationToken) => type switch
        {
            DisputeType.PAYMENT => _paymentChecker.CheckAsync(transaction, cancellationToken),
            DisputeType.DELIVERY => _deliveryChecker.CheckAsync(transaction, cancellationToken),
            DisputeType.WRONG_ITEM => _wrongItemChecker.CheckAsync(transaction, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unhandled dispute type {type} (T58 / 06 §2 DisputeType)."),
        };

    private async Task UpdateActiveDisputeFlagAsync(
        Transaction transaction,
        Guid currentDisputeId,
        CancellationToken cancellationToken)
    {
        // The current dispute is being mutated to a resolved terminal in-flight;
        // the DB still sees its old status, so exclude it from the "any other
        // active" probe to reflect the post-commit state. Local change tracker
        // entries for sibling disputes are observed via EF. Active = OPEN or
        // ESCALATED only — CLOSED (auto) and RESOLVED_FOR_* (admin, WP5) are
        // resolved terminals.
        var otherActiveExist = await _db.Set<Dispute>()
            .AnyAsync(
                d => d.TransactionId == transaction.Id
                     && d.Id != currentDisputeId
                     && (d.Status == DisputeStatus.OPEN || d.Status == DisputeStatus.ESCALATED),
                cancellationToken);

        transaction.HasActiveDispute = otherActiveExist;
    }

    private static OpenDisputeOutcome OpenFailure(
        OpenDisputeStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);

    private static SubmitTxHashOutcome SubmitTxHashFailure(
        SubmitTxHashStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);

    private static EscalateDisputeOutcome EscalateFailure(
        EscalateDisputeStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);
}
