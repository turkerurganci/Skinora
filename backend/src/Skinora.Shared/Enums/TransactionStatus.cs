namespace Skinora.Shared.Enums;

public enum TransactionStatus
{
    CREATED,
    ACCEPTED,

    // The seller confirmed the item is still in their inventory and tradeable,
    // and the buyer's inventory baseline was captured. The deposit address is
    // only revealed to the buyer from this state onwards (02 §2.2 step 3).
    SELLER_CONFIRMED,

    // Payment verified on-chain and held in escrow. The seller must now send
    // the item DIRECTLY to the buyer — the platform is not a party to that
    // trade and never holds the item (02 §2.1).
    PAYMENT_RECEIVED,

    // Delivery verified (02 §9.2). The transaction now enters the settlement
    // period: no payout happens until Steam's trade reversal window closes and
    // the item is confirmed to still be with the buyer (02 §4.5.1).
    ITEM_DELIVERED,

    COMPLETED,
    CANCELLED_TIMEOUT,
    CANCELLED_SELLER,
    CANCELLED_BUYER,
    CANCELLED_ADMIN,
    FLAGGED,

    // Terminal — the transaction was unwound and the buyer refunded. Two
    // producers: buyer-favor admin dispute resolution (WP5 / T58) and detected
    // trade reversal at settlement (02 §4.5.1). Kept distinct from
    // CANCELLED_ADMIN so refunds stay first-class in reporting.
    REFUNDED
}
