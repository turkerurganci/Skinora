namespace Skinora.Transactions.Application.PayoutIssues;

/// <summary>
/// Verifies a seller-reported payout problem against the on-chain record.
/// The production implementation is owned by the Tron blockchain sidecar
/// (T64–T69 forward devir). T60 ships a stub
/// (<see cref="StubPayoutVerifier"/>) so the orchestration in
/// <see cref="PayoutIssueService"/> is end-to-end testable today.
/// </summary>
/// <remarks>
/// The verifier does not mutate state — <see cref="PayoutIssueService"/>
/// translates the result into the appropriate
/// <see cref="Skinora.Shared.Enums.PayoutIssueStatus"/> transition + outbox
/// event emission inside the same DB transaction.
/// </remarks>
public interface IPayoutVerifier
{
    /// <summary>
    /// Inspects the chain for the payout associated with
    /// <paramref name="transactionId"/>. <paramref name="expectedPayoutTxHash"/>
    /// is the hash the platform recorded when the SELLER_PAYOUT was broadcast
    /// — null is acceptable when no broadcast happened (corrupt state, very
    /// old transactions). The verifier returns its best determination; the
    /// service decides which terminal state applies.
    /// </summary>
    Task<PayoutVerificationResult> VerifyAsync(
        Guid transactionId,
        string? expectedPayoutTxHash,
        CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated outcome of <see cref="IPayoutVerifier.VerifyAsync"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="Confirmed"/>: the chain confirmed the payout. The
///   service marks the issue RESOLVED and stamps
///   <c>PayoutTxHash</c> + <c>ResolvedAt</c>.</item>
///   <item><see cref="AnomalyDetected"/>: the chain reports a divergence
///   (reorg, missing tx, mismatched amount). Operator review is required —
///   the service moves to ESCALATED.</item>
///   <item><see cref="StillPending"/>: the broadcast looks healthy but
///   confirmations are still accumulating. The service moves to
///   RETRY_SCHEDULED; a follow-up job (forward devir) will re-verify.</item>
///   <item><see cref="UnableToVerify"/>: the verifier could not reach the
///   chain (sidecar unreachable, tx hash null, transient transport error).
///   To honor 02 §10.3 "Eskalasyon: Otomatik çözüm başarısız olursa admin'e
///   eskale edilir", the service treats this as ESCALATED.</item>
/// </list>
/// </remarks>
public enum PayoutVerificationOutcome
{
    Confirmed,
    AnomalyDetected,
    StillPending,
    UnableToVerify,
}

/// <summary>
/// Result of <see cref="IPayoutVerifier.VerifyAsync"/>. <see cref="VerifiedTxHash"/>
/// is non-null only when <see cref="Outcome"/> is
/// <see cref="PayoutVerificationOutcome.Confirmed"/>; the service uses it to
/// stamp <c>SellerPayoutIssue.PayoutTxHash</c>.
/// </summary>
public sealed record PayoutVerificationResult(
    PayoutVerificationOutcome Outcome,
    string? VerifiedTxHash,
    string Message);
