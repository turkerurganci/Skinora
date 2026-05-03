namespace Skinora.Transactions.Application.GasFee;

/// <summary>
/// Wraps <see cref="Skinora.Transactions.Domain.Calculations.FinancialCalculator"/>
/// with live <see cref="IGasFeeSettingsProvider"/> ratios so callers (refund
/// orchestrator T57+, blockchain sidecar consumer T73) decide
/// "refund vs admin alert" against the operational configuration instead of
/// the formula-side defaults.
/// </summary>
/// <remarks>
/// Service is pure beyond the SystemSetting read — no AuditLog write, no
/// outbox enqueue, no <c>SaveChanges</c>. <see cref="IRefundBlockedAlertService"/>
/// is the side-effect tier; callers compose:
/// <code>
/// var decision = await _refundDecision.ResolveBuyerRefundAsync(...);
/// if (decision.Outcome == RefundOutcome.Block)
///     await _refundAlerts.RaiseAsync(transaction, decision, ct);
/// </code>
/// Splitting decision from alert keeps the read path cheap (the eligibility
/// preview also needs to know whether a hypothetical refund would clear the
/// threshold without ever writing an audit row).
/// </remarks>
public interface IRefundDecisionService
{
    /// <summary>
    /// Decide what to do with a full-cancellation refund (02 §4.6, 09 §14.4):
    /// <c>net = totalPaid − gasFee</c>, refund only if
    /// <c>net ≥ gasFee × min_refund_threshold_ratio</c>.
    /// </summary>
    Task<RefundDecision> ResolveBuyerRefundAsync(
        decimal totalPaid,
        decimal gasFee,
        CancellationToken cancellationToken);

    /// <summary>
    /// Decide what to do with the residue of an overpayment (09 §14.4):
    /// <c>net = (received − expected) − gasFee</c>, refund only if
    /// <c>net ≥ gasFee × min_refund_threshold_ratio</c>. When
    /// <paramref name="received"/> ≤ <paramref name="expected"/> the
    /// overpayment is zero — outcome is <see cref="RefundOutcome.Block"/>
    /// with reason <see cref="Skinora.Shared.Events.RefundBlockedReason.NegativeAmount"/>.
    /// </summary>
    Task<RefundDecision> ResolveOverpaymentRefundAsync(
        decimal expected,
        decimal received,
        decimal gasFee,
        CancellationToken cancellationToken);

    /// <summary>
    /// Compute the seller payout under the gas-fee-protection rule
    /// (02 §4.7, 09 §14.4). Pure delegation to
    /// <see cref="Skinora.Transactions.Domain.Calculations.FinancialCalculator.CalculateSellerPayout"/>
    /// with the live <c>gas_fee_protection_ratio</c>.
    /// </summary>
    Task<decimal> ResolveSellerPayoutAsync(
        decimal price,
        decimal commissionAmount,
        decimal gasFee,
        CancellationToken cancellationToken);
}

/// <summary>
/// Outcome of a refund decision — caller branches on this before
/// invoking blockchain transfer or admin-alert sinks.
/// </summary>
public enum RefundOutcome
{
    /// <summary>Net refund clears the minimum threshold; safe to send.</summary>
    Refund,

    /// <summary>Net refund below threshold (or negative) — the platform
    /// suppresses the transfer and routes to admin alert (09 §14.4).</summary>
    Block,
}

/// <summary>
/// Refund decision snapshot — every numeric is in <c>decimal</c>
/// (09 §14.1); rounding happens only at the storage/wire boundary.
/// </summary>
/// <param name="Outcome">Whether the refund proceeds or is blocked.</param>
/// <param name="NetRefund">Computed net refund (<c>totalPaid − gasFee</c>);
/// may be negative when the gas fee exceeds the source amount.</param>
/// <param name="Threshold">Minimum payable refund floor
/// (<c>gasFee × MinRefundThresholdRatio</c>) at decision time.</param>
/// <param name="GasFee">Gas fee fed into the decision — echoed so the alert
/// payload can be assembled without re-passing the input.</param>
/// <param name="TotalPaid">Source amount the refund was being computed from
/// (full <c>TotalAmount</c> for cancellation, overpayment delta otherwise).</param>
/// <param name="Reason">Populated when <see cref="RefundOutcome.Block"/>;
/// <c>null</c> on the refund path.</param>
public sealed record RefundDecision(
    RefundOutcome Outcome,
    decimal NetRefund,
    decimal Threshold,
    decimal GasFee,
    decimal TotalPaid,
    Skinora.Shared.Events.RefundBlockedReason? Reason);
