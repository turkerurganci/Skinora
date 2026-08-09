using Skinora.Shared.Enums;

namespace Skinora.Shared.Domain;

/// <summary>
/// Canonical per-type dispute eligibility matrix (02 §10, 07 §7.5/§7.8).
/// Single source of truth shared by the dispute-open guard
/// (<c>Skinora.Disputes.DisputeService</c>) and the <c>canDispute</c> /
/// <c>disputableTypes</c> envelope (<c>Skinora.Transactions.TransactionDetailService</c>).
/// Lives in Shared because Transactions cannot reference Disputes (the
/// dependency direction is Disputes → Transactions).
/// </summary>
public static class DisputeEligibility
{
    /// <summary>
    /// Transaction states in which each dispute type may be opened by the buyer
    /// (02 §10.1).
    /// </summary>
    public static readonly IReadOnlyDictionary<DisputeType, TransactionStatus[]> AllowedStatesByType =
        new Dictionary<DisputeType, TransactionStatus[]>
        {
            [DisputeType.PAYMENT] = new[]
            {
                TransactionStatus.SELLER_CONFIRMED,
                TransactionStatus.PAYMENT_RECEIVED,
            },
            [DisputeType.DELIVERY] = new[]
            {
                TransactionStatus.PAYMENT_RECEIVED,
                TransactionStatus.ITEM_DELIVERED,
            },

            // PAYMENT_RECEIVED is deliberately included (v3.0): if the seller
            // sends a different item, the expected class count never rises, so
            // the transaction never reaches ITEM_DELIVERED. The buyer must be
            // able to raise a wrong-item dispute from that state too, otherwise
            // the case is only reachable via timeout (02 §10.1, 03 §6.3).
            [DisputeType.WRONG_ITEM] = new[]
            {
                TransactionStatus.PAYMENT_RECEIVED,
                TransactionStatus.ITEM_DELIVERED,
            },
        };

    /// <summary>
    /// Dispute types eligible to be opened against a transaction currently in
    /// <paramref name="status"/>. Pure state check — HasActiveDispute and the
    /// duplicate-type guard are layered on by the caller (07 §7.8).
    /// </summary>
    public static IReadOnlyList<DisputeType> DisputableTypesFor(TransactionStatus status) =>
        AllowedStatesByType
            .Where(kvp => kvp.Value.Contains(status))
            .Select(kvp => kvp.Key)
            .ToArray();
}
