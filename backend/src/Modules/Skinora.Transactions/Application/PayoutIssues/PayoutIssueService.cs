using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.PayoutIssues;

/// <summary>
/// T60 — 07 §7.11, 02 §10.3, 06 §3.8a, 03 §2.4a Senaryo A implementation.
/// All side effects (issue row insert + state transition, outbox event emit)
/// land inside a single <see cref="DbContext.SaveChangesAsync"/> so the
/// payout-issue lifecycle is atomic with the emitted events (09 §13.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>State transitions in one request:</b> REPORTED is the entry point. The
/// verifier outcome decides the terminal state in the same atomic
/// SaveChanges:
/// </para>
/// <list type="bullet">
///   <item><c>Confirmed</c> → RESOLVED + <c>PayoutTxHash</c> +
///   <c>ResolvedAt</c>; emits <see cref="SellerPayoutIssueResolvedEvent"/>.</item>
///   <item><c>AnomalyDetected</c> / <c>UnableToVerify</c> → ESCALATED
///   (<c>EscalatedToAdminId</c> resolved via
///   <see cref="IPayoutEscalationAdminResolver"/>); emits
///   <see cref="SellerPayoutIssueEscalatedEvent"/>.</item>
///   <item><c>StillPending</c> → RETRY_SCHEDULED with <c>RetryCount = 1</c>;
///   emits <see cref="SellerPayoutIssueReportedEvent"/> so the future retry
///   pipeline (06 §3.8 BlockchainTransaction retry, T-future) has a hook to
///   pick up.</item>
/// </list>
/// <para>
/// <b>Active-issue guard:</b> a defensive pre-check via
/// <c>IgnoreQueryFilters</c> + <c>VerificationStatus != RESOLVED</c> mirrors
/// the <c>UQ_SellerPayoutIssues_TransactionId_Active</c> filtered unique
/// index (06 §3.8a "Tek aktif issue kuralı"). The pre-check surfaces
/// <c>ISSUE_ALREADY_REPORTED</c> instead of letting the DB raise a generic
/// conflict.
/// </para>
/// <para>
/// <b>Detail validation:</b> 07 §7.11 requires <c>detail</c> ≥10 chars
/// trimmed. Failures return <c>VALIDATION_ERROR</c>.
/// </para>
/// <para>
/// <b>State guard ordering:</b> 07 §7.11 lists 409
/// <c>TRANSACTION_NOT_COMPLETED</c> and 409 <c>ISSUE_ALREADY_REPORTED</c>
/// alongside 403 <c>NOT_SELLER</c>. The service evaluates the guards in this
/// order — Found → NotSeller → NotCompleted → AlreadyReported → Validation —
/// so authorization failures never leak whether the underlying transaction
/// is in a refusable state.
/// </para>
/// </remarks>
public sealed class PayoutIssueService : IPayoutIssueService
{
    public const int MinDetailLength = 10;

    private readonly AppDbContext _db;
    private readonly IOutboxService _outbox;
    private readonly IPayoutVerifier _verifier;
    private readonly IPayoutEscalationAdminResolver _adminResolver;
    private readonly TimeProvider _clock;

    public PayoutIssueService(
        AppDbContext db,
        IOutboxService outbox,
        IPayoutVerifier verifier,
        IPayoutEscalationAdminResolver adminResolver,
        TimeProvider clock)
    {
        _db = db;
        _outbox = outbox;
        _verifier = verifier;
        _adminResolver = adminResolver;
        _clock = clock;
    }

    public async Task<ReportPayoutIssueOutcome> ReportAsync(
        Guid callerUserId,
        Guid transactionId,
        ReportPayoutIssueRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Stage 1 — load transaction.
        var transaction = await _db.Set<Transaction>()
            .FirstOrDefaultAsync(
                t => t.Id == transactionId && !t.IsDeleted, cancellationToken);
        if (transaction is null)
            return Failure(ReportPayoutIssueStatus.NotFound,
                PayoutIssueErrorCodes.TransactionNotFound, "Transaction not found.");

        // Stage 2 — seller guard (only the seller can report per 02 §10.3).
        if (transaction.SellerId != callerUserId)
            return Failure(ReportPayoutIssueStatus.NotSeller,
                PayoutIssueErrorCodes.NotSeller,
                "Only the seller can report a payout issue on this transaction.");

        // Stage 3 — COMPLETED state guard (07 §7.11 yalnızca COMPLETED işlemler).
        if (transaction.Status != TransactionStatus.COMPLETED)
            return Failure(ReportPayoutIssueStatus.TransactionNotCompleted,
                PayoutIssueErrorCodes.TransactionNotCompleted,
                "Payout issue can only be reported on COMPLETED transactions (07 §7.11).");

        // Stage 4 — single active issue guard. IgnoreQueryFilters defensively
        // so a soft-deleted active row still blocks reporting; the runtime
        // filtered UQ catches any race that slips past this check.
        var hasActive = await _db.Set<SellerPayoutIssue>()
            .IgnoreQueryFilters()
            .AnyAsync(
                i => i.TransactionId == transactionId
                     && i.VerificationStatus != PayoutIssueStatus.RESOLVED,
                cancellationToken);
        if (hasActive)
            return Failure(ReportPayoutIssueStatus.IssueAlreadyReported,
                PayoutIssueErrorCodes.IssueAlreadyReported,
                "An active payout issue is already open for this transaction (06 §3.8a).");

        // Stage 5 — detail validation (≥10 chars trimmed per 07 §7.11).
        var trimmedDetail = (request.Detail ?? string.Empty).Trim();
        if (trimmedDetail.Length < MinDetailLength)
            return Failure(ReportPayoutIssueStatus.ValidationFailed,
                PayoutIssueErrorCodes.ValidationError,
                $"detail must be at least {MinDetailLength} characters (07 §7.11).");

        // Stage 6 — build issue at REPORTED, attach to context.
        var now = _clock.GetUtcNow().UtcDateTime;
        var issue = new SellerPayoutIssue
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            SellerId = callerUserId,
            Detail = trimmedDetail,
            VerificationStatus = PayoutIssueStatus.REPORTED,
            RetryCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Set<SellerPayoutIssue>().Add(issue);

        // Stage 7 — run verifier inline, then transition state in the same
        // SaveChanges. The recorded payout tx hash on the COMPLETED transaction
        // is read off the BlockchainTransaction SELLER_PAYOUT row; null is
        // acceptable when the platform never recorded a broadcast (corrupt
        // historical state) — the verifier handles that case as
        // UnableToVerify.
        var expectedHash = await _db.Set<BlockchainTransaction>()
            .AsNoTracking()
            .Where(b => b.TransactionId == transactionId
                        && b.Type == BlockchainTransactionType.SELLER_PAYOUT)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => b.TxHash)
            .FirstOrDefaultAsync(cancellationToken);

        var verification = await _verifier.VerifyAsync(
            transactionId, expectedHash, cancellationToken);

        var resultMessage = await ApplyVerificationOutcomeAsync(
            issue, verification, now, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        // Stage 9 — assemble response per 07 §7.11. Status reflects the
        // post-verification state the row is actually in. CreatedAt is
        // sourced from the clock rather than the entity because the audit
        // pipeline overwrites the entity's CreatedAt with DateTime.UtcNow
        // during SaveChanges (mirrors DisputeService — small ms-scale skew
        // between persisted row and response is acceptable).
        var body = new ReportPayoutIssueResponse(
            IssueId: issue.Id,
            Status: issue.VerificationStatus,
            CreatedAt: now,
            Message: resultMessage);

        return new ReportPayoutIssueOutcome(
            Status: ReportPayoutIssueStatus.Reported,
            Body: body,
            ErrorCode: null,
            ErrorMessage: null);
    }

    private async Task<string> ApplyVerificationOutcomeAsync(
        SellerPayoutIssue issue,
        PayoutVerificationResult verification,
        DateTime now,
        CancellationToken cancellationToken)
    {
        switch (verification.Outcome)
        {
            case PayoutVerificationOutcome.Confirmed:
                issue.VerificationStatus = PayoutIssueStatus.RESOLVED;
                issue.PayoutTxHash = verification.VerifiedTxHash;
                issue.ResolvedAt = now;
                await _outbox.PublishAsync(
                    new SellerPayoutIssueResolvedEvent(
                        EventId: Guid.NewGuid(),
                        IssueId: issue.Id,
                        TransactionId: issue.TransactionId,
                        SellerId: issue.SellerId,
                        PayoutTxHash: verification.VerifiedTxHash,
                        ResolvedByAdminId: null,
                        OccurredAt: now),
                    cancellationToken);
                return BuildResolvedMessage(verification);

            case PayoutVerificationOutcome.AnomalyDetected:
            case PayoutVerificationOutcome.UnableToVerify:
                var adminId = await _adminResolver.ResolveAdminUserIdAsync(cancellationToken);
                if (adminId is null)
                    throw new InvalidOperationException(
                        "No admin user available to escalate SellerPayoutIssue to. "
                        + "06 §3.8a CK_SellerPayoutIssues_Status_Invariants requires "
                        + "EscalatedToAdminId NOT NULL when ESCALATED.");
                issue.VerificationStatus = PayoutIssueStatus.ESCALATED;
                issue.EscalatedToAdminId = adminId;
                await _outbox.PublishAsync(
                    new SellerPayoutIssueEscalatedEvent(
                        EventId: Guid.NewGuid(),
                        IssueId: issue.Id,
                        TransactionId: issue.TransactionId,
                        SellerId: issue.SellerId,
                        EscalatedToAdminId: adminId.Value,
                        VerificationMessage: verification.Message,
                        OccurredAt: now),
                    cancellationToken);
                return verification.Message;

            case PayoutVerificationOutcome.StillPending:
                issue.VerificationStatus = PayoutIssueStatus.RETRY_SCHEDULED;
                issue.RetryCount = 1;
                await _outbox.PublishAsync(
                    new SellerPayoutIssueReportedEvent(
                        EventId: Guid.NewGuid(),
                        IssueId: issue.Id,
                        TransactionId: issue.TransactionId,
                        SellerId: issue.SellerId,
                        OccurredAt: now),
                    cancellationToken);
                return verification.Message;

            default:
                throw new InvalidOperationException(
                    $"Unhandled verification outcome {verification.Outcome}.");
        }
    }

    private static string BuildResolvedMessage(PayoutVerificationResult verification)
    {
        if (!string.IsNullOrWhiteSpace(verification.VerifiedTxHash))
            return $"Ödeme blockchain üzerinde doğrulandı: {verification.VerifiedTxHash}";
        return verification.Message;
    }

    private static ReportPayoutIssueOutcome Failure(
        ReportPayoutIssueStatus status, string errorCode, string message)
        => new(status, Body: null, ErrorCode: errorCode, ErrorMessage: message);
}
