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
    /// Transaction states in which each dispute type may be opened by the buyer.
    /// PAYMENT → ITEM_ESCROWED/PAYMENT_RECEIVED; DELIVERY →
    /// TRADE_OFFER_SENT_TO_BUYER/ITEM_DELIVERED; WRONG_ITEM → ITEM_DELIVERED.
    /// </summary>
    public static readonly IReadOnlyDictionary<DisputeType, TransactionStatus[]> AllowedStatesByType =
        new Dictionary<DisputeType, TransactionStatus[]>
        {
            [DisputeType.PAYMENT] = new[]
            {
                TransactionStatus.ITEM_ESCROWED,
                TransactionStatus.PAYMENT_RECEIVED,
            },
            [DisputeType.DELIVERY] = new[]
            {
                TransactionStatus.TRADE_OFFER_SENT_TO_BUYER,
                TransactionStatus.ITEM_DELIVERED,
            },
            [DisputeType.WRONG_ITEM] = new[]
            {
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
