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
    // drawn down by outbound payouts/refunds and hot→cold transfers. The
    // sweep dispatcher itself (PaymentReceivedEvent consumer) is T-future;
    // until then the column simply stores 0 SWEEP rows and the reconciliation
    // hot wallet calculation collapses to outflows-only.
    SWEEP
}
