namespace Skinora.Shared.Enums;

/// <summary>
/// Identifies which lifecycle deadline elapsed when a transaction times out
/// (03 §4.1–§4.4). The phase determines downstream side effects: refund
/// requirements, late-payment monitoring, and per-recipient notification text.
/// </summary>
public enum TimeoutPhase
{
    /// <summary>03 §4.1 — buyer did not accept within <c>AcceptDeadline</c>. No refund needed.</summary>
    Accept,

    /// <summary>03 §4.2 — seller did not action the trade offer within <c>TradeOfferToSellerDeadline</c>. No refund needed.</summary>
    TradeOfferToSeller,

    /// <summary>03 §4.3 — buyer did not pay within <c>PaymentDeadline</c>. Item is refunded to the seller; platform keeps watching for late payment.</summary>
    Payment,

    /// <summary>03 §4.4 — buyer did not accept the delivery trade offer within <c>TradeOfferToBuyerDeadline</c>. Item is refunded to the seller and payment is refunded to the buyer.</summary>
    Delivery,
}
