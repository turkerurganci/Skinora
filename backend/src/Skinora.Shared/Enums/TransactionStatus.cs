namespace Skinora.Shared.Enums;

public enum TransactionStatus
{
    CREATED,
    ACCEPTED,
    TRADE_OFFER_SENT_TO_SELLER,
    ITEM_ESCROWED,
    PAYMENT_RECEIVED,
    TRADE_OFFER_SENT_TO_BUYER,
    ITEM_DELIVERED,
    COMPLETED,
    CANCELLED_TIMEOUT,
    CANCELLED_SELLER,
    CANCELLED_BUYER,
    CANCELLED_ADMIN,
    FLAGGED,

    // Terminal — buyer-favor admin dispute resolution (WP5 / T58). The
    // transaction is unwound and the buyer refunded; distinct from
    // CANCELLED_ADMIN so dispute-driven refunds are first-class in reporting.
    REFUNDED
}
