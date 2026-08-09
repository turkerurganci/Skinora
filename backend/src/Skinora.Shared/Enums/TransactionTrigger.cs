namespace Skinora.Shared.Enums;

/// <summary>
/// Transaction state machine triggers (05 §4.2 transition table).
/// </summary>
public enum TransactionTrigger
{
    BuyerAccept,

    // Seller confirms readiness to send. Guarded by a fresh inventory re-check,
    // the buyer's mobile authenticator probe, and baseline capture (03 §2.3).
    SellerConfirmReady,

    ConfirmPayment,

    // Delivery proven by buyer confirmation and/or inventory evidence
    // (02 §9.2). The guard reads DeliveryEvidence, not an asset id.
    DeliverItem,

    // Settlement passed: the reversal window closed and the item is still with
    // the buyer. Only after this may the seller be paid (02 §4.5.1).
    Complete,

    // Settlement failed: the item is no longer in the buyer's inventory, so the
    // trade was reversed. System-produced only — no user or admin can fire it.
    // The seller is not paid; the buyer is refunded (02 §4.5.1).
    DeliveryReversed,

    Timeout,
    SellerCancel,
    BuyerCancel,
    AdminCancel,
    SellerDecline,
    AdminApprove,
    AdminReject,

    // WP5 / T58 — buyer-favor admin dispute resolution. Permitted from the
    // disputed states (SELLER_CONFIRMED, PAYMENT_RECEIVED, ITEM_DELIVERED)
    // into the terminal REFUNDED state.
    AdminResolveRefund
}
