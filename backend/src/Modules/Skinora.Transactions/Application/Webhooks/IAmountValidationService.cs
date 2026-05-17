using Skinora.Transactions.Domain.Entities;

namespace Skinora.Transactions.Application.Webhooks;

/// <summary>
/// T72 amount validation pipeline (02 §4.4, 08 §3.4) — invoked from
/// <see cref="BlockchainWebhookHandler"/> right after a confirmed buyer
/// payment row is flipped to <c>CONFIRMED</c>, or a <c>WRONG_TOKEN_INCOMING</c>
/// row is written.
/// </summary>
/// <remarks>
/// <para>
/// The service composes existing T53 infrastructure
/// (<see cref="GasFee.IRefundDecisionService"/>,
/// <see cref="GasFee.IRefundBlockedAlertService"/>) with the
/// <see cref="Domain.StateMachine.TransactionStateMachine"/>: correct-amount
/// payments fire <c>ConfirmPayment</c>, mismatched amounts queue refund-intent
/// <see cref="BlockchainTransaction"/> rows at <c>Status=PENDING</c>, and
/// sub-threshold residues surface as admin alerts.
/// </para>
/// <para>
/// The service does <em>not</em> call <c>SaveChangesAsync</c> — the calling
/// webhook handler owns the unit of work so the original
/// <c>BlockchainTransaction</c> flip and the validation side effects commit
/// in a single transaction (mirrors
/// <see cref="GasFee.IRefundDecisionService"/> contract).
/// </para>
/// </remarks>
public interface IAmountValidationService
{
    /// <summary>
    /// Classify a freshly-confirmed buyer payment (expected token) against
    /// the <see cref="PaymentAddress.ExpectedAmount"/> snapshot and apply the
    /// resulting side effects in the current EF Core change tracker.
    /// </summary>
    /// <param name="confirmedPayment">
    /// The <see cref="BlockchainTransaction"/> row the caller has just
    /// transitioned to <c>Status=CONFIRMED</c>; expected
    /// <c>Type=BUYER_PAYMENT</c>.
    /// </param>
    /// <param name="correlationId">Webhook correlation id for log fan-out.</param>
    /// <param name="cancellationToken"/>
    Task<AmountValidationOutcome> ValidateConfirmedBuyerPaymentAsync(
        BlockchainTransaction confirmedPayment,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Classify a freshly-recorded wrong-token incoming transfer
    /// (<c>Type=WRONG_TOKEN_INCOMING</c>) against the platform refund
    /// threshold and emit either a refund-intent row or an admin alert
    /// (08 §3.4 wrong-token table).
    /// </summary>
    /// <param name="wrongTokenIncoming">
    /// The <see cref="BlockchainTransaction"/> row the caller has just
    /// persisted at <c>Status=DETECTED</c> with <c>ActualTokenAddress</c>
    /// populated.
    /// </param>
    /// <param name="correlationId">Webhook correlation id for log fan-out.</param>
    /// <param name="cancellationToken"/>
    Task<AmountValidationOutcome> ValidateWrongTokenIncomingAsync(
        BlockchainTransaction wrongTokenIncoming,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Classify a late buyer transfer detected by the T75 post-cancel
    /// monitor (02 §4.4 timeout-sonrası gecikmeli ödeme). Mirrors the
    /// multi-payment branch of <see cref="ValidateConfirmedBuyerPaymentAsync"/>
    /// but emits <c>LATE_PAYMENT_REFUND</c> rather than <c>EXCESS_REFUND</c>
    /// so dispatch + audit can distinguish the two flows.
    /// </summary>
    /// <param name="latePayment">
    /// The <see cref="BlockchainTransaction"/> row the caller has just
    /// persisted at <c>Type=BUYER_PAYMENT</c> / <c>Status=DETECTED</c>.
    /// </param>
    /// <param name="correlationId">Webhook correlation id for log fan-out.</param>
    /// <param name="cancellationToken"/>
    Task<AmountValidationOutcome> ValidateLatePaymentDetectedAsync(
        BlockchainTransaction latePayment,
        string correlationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Classification produced by <see cref="IAmountValidationService"/> for
/// observability — drives log/metric fan-out but is otherwise advisory; all
/// side effects are already applied to the change tracker when the result
/// is returned.
/// </summary>
public enum AmountValidationOutcome
{
    /// <summary>Received amount matched the expected amount exactly →
    /// state machine fired <c>ConfirmPayment</c>.</summary>
    AcceptedExact,

    /// <summary>Received amount &gt; expected → state machine fired
    /// <c>ConfirmPayment</c> and an <c>EXCESS_REFUND</c> row was queued
    /// (or admin alert raised for sub-threshold excess).</summary>
    AcceptedWithExcessRefund,

    /// <summary>Received amount &lt; expected → state stays at
    /// <c>ITEM_ESCROWED</c>, <c>INCORRECT_AMOUNT_REFUND</c> row queued
    /// (or admin alert raised for sub-threshold).</summary>
    Underpaid,

    /// <summary>Payment arrived after the transaction left
    /// <c>ITEM_ESCROWED</c>; entire <c>received</c> amount queued as
    /// <c>EXCESS_REFUND</c> (or admin alert).</summary>
    MultiPaymentRefunded,

    /// <summary>Wrong-token transfer above threshold → refund row queued.</summary>
    WrongTokenRefundQueued,

    /// <summary>Wrong-token transfer below threshold → admin alert only.</summary>
    WrongTokenAdminAlert,

    /// <summary>Late buyer transfer above threshold (T75) → refund row queued
    /// at <c>LATE_PAYMENT_REFUND</c>.</summary>
    LatePaymentRefundQueued,

    /// <summary>Late buyer transfer below threshold (T75) → admin alert only,
    /// no refund attempted (08 §3.4 minimum eşik).</summary>
    LatePaymentAdminAlert,

    /// <summary>State machine refused the trigger (emergency hold, cancelled,
    /// terminal state). No refund row written; admin operations resume on
    /// hold release / next manual review.</summary>
    StateMachineRejected,

    /// <summary>Required navigation (PaymentAddress or Transaction) was
    /// missing — defensive branch; caller treats as <c>Unknown</c>.</summary>
    MissingNavigation,
}
