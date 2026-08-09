namespace Skinora.Shared.Enums;

/// <summary>
/// Identifies which lifecycle deadline elapsed when a transaction times out
/// (03 §4.1–§4.4). The phase determines downstream side effects: refund
/// requirements, late-payment monitoring, per-recipient notification text, and
/// which party the timeout is attributed to for reputation (02 §3.1).
/// </summary>
public enum TimeoutPhase
{
    /// <summary>03 §4.1 — buyer did not accept within <c>AcceptDeadline</c>. No refund needed. Attributed to the buyer.</summary>
    Accept,

    /// <summary>03 §4.2 — seller did not confirm readiness within <c>SellerConfirmDeadline</c>. No refund needed (no money has moved). Attributed to the seller.</summary>
    SellerConfirm,

    /// <summary>03 §4.3 — buyer did not pay within <c>PaymentDeadline</c>. No item refund exists in the P2P model; the platform keeps watching for late payment. Attributed to the buyer.</summary>
    Payment,

    /// <summary>
    /// 03 §4.4 — the seller did not deliver within <c>DeliveryDeadline</c>. Payment is refunded to the buyer.
    /// Attributed to the <b>seller</b>: in the custodial model this phase waited on the buyer accepting a
    /// platform-sent offer, but in P2P the seller sends the trade, so the delay is theirs (02 §3.1).
    /// Before cancelling, a final delivery verification runs — if evidence is found the transaction is
    /// delivered instead of cancelled, which prevents an unfair refund when the seller did send the item
    /// but the buyer never confirmed (05 §4.4).
    /// </summary>
    Delivery,
}
