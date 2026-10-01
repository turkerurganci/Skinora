using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by <c>SellerPayoutQueueJob</c> when a delivered, settled and swept
/// transaction's seller payout has come out non-positive for the third time in
/// a row (02 §4.7 "satıcıya kalan tutar sıfır ya da altındaysa",
/// PayoutStallsOnNonPositiveNet). The runtime gas estimate exceeded the price
/// plus the platform's protection share, so nothing could be sent.
/// </summary>
/// <remarks>
/// The job keeps retrying on its own backoff (1 h → 4 h → 12 h → every 24 h):
/// the estimate falls as the hot wallet's Energy regenerates, and an operator
/// who restores the payout share of the delegation (DEPLOY_RUNBOOK §C.2 step 8)
/// lets the next retry through without any manual action on the transaction.
/// The event is raised once per transaction — the deferral count only grows,
/// so it crosses the threshold exactly once.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction whose payout is deferred.</param>
/// <param name="SellerId">Seller awaiting the payout.</param>
/// <param name="Price">Item price the payout is computed from.</param>
/// <param name="CommissionAmount">Commission the protection threshold is computed from.</param>
/// <param name="GasFeeEstimate">Gas estimate that consumed the price.</param>
/// <param name="ComputedPayout">Net the split produced (zero or negative).</param>
/// <param name="DeferralCount">Deferrals so far, the one that raised this event included.</param>
/// <param name="NextAttemptAt">UTC time of the next retry.</param>
/// <param name="OccurredAt">UTC timestamp the alert was committed.</param>
public record SellerPayoutDeferredEvent(
    Guid EventId,
    Guid TransactionId,
    Guid SellerId,
    decimal Price,
    decimal CommissionAmount,
    decimal GasFeeEstimate,
    decimal ComputedPayout,
    int DeferralCount,
    DateTime NextAttemptAt,
    DateTime OccurredAt) : IDomainEvent;
