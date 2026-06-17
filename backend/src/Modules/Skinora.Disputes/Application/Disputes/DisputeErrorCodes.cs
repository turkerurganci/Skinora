namespace Skinora.Disputes.Application.Disputes;

/// <summary>
/// Error code constants surfaced by the T58 dispute pipeline. Mirrors the
/// strings listed under 07 §7.8–§7.10 "Hatalar" so callers can pattern-match
/// without a separate enum.
/// </summary>
public static class DisputeErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string NotBuyer = "NOT_BUYER";
    public const string TransactionNotFound = "TRANSACTION_NOT_FOUND";
    public const string DisputeNotFound = "DISPUTE_NOT_FOUND";
    public const string InvalidStateTransition = "INVALID_STATE_TRANSITION";
    public const string DuplicateDispute = "DUPLICATE_DISPUTE";

    // T9 — submit-txhash (07 §7.9).
    public const string NotPaymentDispute = "NOT_PAYMENT_DISPUTE";
    public const string DisputeClosed = "DISPUTE_CLOSED";

    // T10 — escalate (07 §7.10).
    public const string AlreadyEscalated = "ALREADY_ESCALATED";

    // WP5 — admin dispute resolution (07 §9.x).
    public const string NotEscalated = "DISPUTE_NOT_ESCALATED";
    public const string TransactionOnHold = "TRANSACTION_ON_HOLD";
}
