namespace Skinora.Shared.Enums;

public enum BlockchainTransactionType
{
    BUYER_PAYMENT,
    SELLER_PAYOUT,
    BUYER_REFUND,
    EXCESS_REFUND,
    WRONG_TOKEN_INCOMING,
    WRONG_TOKEN_REFUND,
    SPAM_TOKEN_INCOMING,
    LATE_PAYMENT_REFUND,
    INCORRECT_AMOUNT_REFUND,

    // Deposit address → hot wallet sweep ledger entry (05 §3.3). Reconciliation
    // (T76) sums these to derive the hot wallet's expected balance: any payment
    // that has cleared into the deposit address ultimately lands here, then is
    // drawn down by outbound payouts/refunds and hot→cold transfers. Produced by
    // WP3's SweepQueueJob. NOTE (WP3, owner decision 2026-06-15): although 05
    // §3.3 names PaymentReceivedEvent as the sweep trigger, the sweep is
    // deferred to the ITEM_DELIVERED milestone — the deposit-sourced buyer
    // refund (WP2) must keep its funds until the buyer-refund window closes
    // (05 §3.3 line 323), so sweeping eagerly at PAYMENT_RECEIVED would break
    // the common cancelled-after-payment refund.
    SWEEP
}
