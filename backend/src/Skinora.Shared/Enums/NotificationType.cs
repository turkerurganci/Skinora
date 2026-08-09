namespace Skinora.Shared.Enums;

public enum NotificationType
{
    TRANSACTION_INVITE,
    BUYER_ACCEPTED,

    // v3.0 — the seller confirmed readiness, so the deposit address is now open
    // to the buyer (02 §2.2 step 3). Replaces ITEM_ESCROWED: nothing is escrowed
    // at this point except, shortly, the money.
    PAYMENT_WINDOW_OPEN,

    PAYMENT_RECEIVED,

    // v3.0 — payment is in escrow and the SELLER must now send the item
    // directly to the buyer. Replaces TRADE_OFFER_SENT_TO_BUYER, which targeted
    // the buyer; the recipient of this notification flipped sides.
    DELIVERY_EXPECTED,

    TRANSACTION_COMPLETED,
    SELLER_PAYMENT_SENT,
    TIMEOUT_WARNING,
    TRANSACTION_CANCELLED,
    TRANSACTION_FLAGGED,
    PAYMENT_INCORRECT,
    LATE_PAYMENT_REFUNDED,

    // ITEM_RETURNED removed in v3.0 — the platform never holds the item, so it
    // can never return one (02 §9).
    PAYMENT_REFUNDED,
    DISPUTE_RESULT,
    FLAG_RESOLVED,
    ADMIN_FLAG_ALERT,
    ADMIN_ESCALATION,
    ADMIN_PAYMENT_FAILURE,

    // ADMIN_STEAM_BOT_ISSUE removed in v3.0 — the platform runs no Steam bots
    // (02 §15, 05 §3.2).
    EMERGENCY_HOLD_APPLIED,
    EMERGENCY_HOLD_RELEASED,

    // --- T72: Blockchain amount validation outcomes (02 §4.4, 08 §3.4) ---
    INSUFFICIENT_PAYMENT,
    OVERPAYMENT_REFUNDED,
    WRONG_TOKEN_REFUND,

    // --- T105a: Account suspension lifecycle (02 §14.0/§16.2, 03 §2.1/§8.3) ---
    ACCOUNT_SUSPENDED,
    ACCOUNT_UNSUSPENDED,

    // --- WP16: platform health probe alert (05 §4.4, 02 §3.3) ---
    // Admin-only operational alert raised when the periodic health probe detects
    // a Steam/blockchain sidecar outage (or its recovery). Alert-only — the admin
    // decides whether to apply a maintenance freeze (WP7). Fanned out to every
    // admin via the WP8 AdminBroadcast pattern; pairs with the
    // PLATFORM_OUTAGE_DETECTED audit row.
    ADMIN_PLATFORM_OUTAGE
}
