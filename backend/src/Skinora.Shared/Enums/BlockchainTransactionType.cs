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
    SWEEP,

    // T160c — deposit → hot wallet transfer of the refund network fees that
    // were withheld from the buyer's refunds (02 §4.7, 05 §3.3) and that the
    // transaction's own SWEEP could not carry: the transaction never reached a
    // sweep (cancelled / refunded), the fee is in another token (a wrong-token
    // refund, USDC), or the refund confirmed after the sweep was queued.
    // Produced by RefundFeeSweepQueueJob; same deposit-anchored shape as SWEEP
    // (PaymentAddressId NOT NULL) and the same sidecar endpoint, but a
    // separate type so the rules that read "the" SWEEP (payout gate, refund
    // source, monitor disarm) never see it.
    REFUND_FEE_SWEEP
}
