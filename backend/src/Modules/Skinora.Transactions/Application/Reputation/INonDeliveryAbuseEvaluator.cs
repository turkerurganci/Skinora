namespace Skinora.Transactions.Application.Reputation;

/// <summary>
/// 02 §14.2 — the sanction for a seller who repeatedly fails a buyer who has
/// already paid: an <c>ABNORMAL_BEHAVIOR</c> account flag on the first repeat
/// inside the rolling window, automatic suspension on the next (thresholds:
/// 02 §16.2 "Teslimat ihlali eşikleri").
/// </summary>
/// <remarks>
/// <para>
/// Evaluated per TRANSITION, not per user. The caller passes the transaction
/// that has just gone terminal and the evaluator acts only if that transaction
/// is itself a non-delivery event. Evaluating on every reputation refresh would
/// re-suspend a seller an admin has just cleared, the next time any unrelated
/// transaction of theirs completed — the window would still hold the same
/// three events. Keyed to the event, a suspension can only follow a NEW
/// failure.
/// </para>
/// <para>
/// Stages writes only; the caller owns <c>SaveChangesAsync</c> and MUST have
/// flushed the terminal transition (and, for cancellations, its
/// <c>TransactionHistory</c> row) first — the event query reads with
/// <c>AsNoTracking</c>, exactly like <see cref="CancelCooldownEvaluator"/>.
/// </para>
/// </remarks>
public interface INonDeliveryAbuseEvaluator
{
    Task<NonDeliveryAbuseOutcome> EvaluateAsync(Guid transactionId, CancellationToken cancellationToken);
}

/// <summary>What the evaluation did. Returned for tests and logs; callers do not branch on it.</summary>
public enum NonDeliveryAbuseAction
{
    /// <summary>The transaction is not a non-delivery event (or does not exist).</summary>
    NotANonDeliveryEvent,

    /// <summary>A threshold is unconfigured — the rule does not run.</summary>
    RuleDisabled,

    /// <summary>Counted, below the flag threshold.</summary>
    BelowThreshold,

    /// <summary>An <c>ABNORMAL_BEHAVIOR</c> account flag was staged.</summary>
    Flagged,

    /// <summary>At the flag threshold, but a PENDING account flag of that type already awaits review.</summary>
    FlagAlreadyPending,

    /// <summary>The account was suspended (and a flag staged unless one was already pending).</summary>
    Suspended,

    /// <summary>At the suspension threshold, but the account is already suspended.</summary>
    AlreadySuspended,
}

/// <param name="Action">What was done.</param>
/// <param name="EventCount">Non-delivery events inside the window, the triggering one included (0 when not counted).</param>
public sealed record NonDeliveryAbuseOutcome(NonDeliveryAbuseAction Action, int EventCount);
