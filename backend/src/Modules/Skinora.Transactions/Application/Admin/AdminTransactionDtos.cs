using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Admin;

// ---------- AD19 — POST /admin/transactions/:id/cancel ----------

public sealed record AdminCancelTransactionRequest(string? Reason);

public sealed record AdminCancelTransactionResponse(
    TransactionStatus Status,
    DateTime CancelledAt,
    bool ItemReturned,
    bool PaymentRefunded);

public enum AdminCancelTransactionStatus
{
    Cancelled,
    NotFound,
    ValidationFailed,
    InvalidStateTransition,
    CannotCancelAtDeliveryStage,
}

public sealed record AdminCancelTransactionOutcome(
    AdminCancelTransactionStatus Status,
    AdminCancelTransactionResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

// ---------- AD19b — POST /admin/transactions/:id/emergency-hold ----------

public sealed record ApplyEmergencyHoldRequest(string? Reason);

public sealed record ApplyEmergencyHoldResponse(
    string Status,
    DateTime FrozenAt,
    TransactionStatus PreviousStatus);

public enum ApplyEmergencyHoldStatus
{
    Applied,
    NotFound,
    ValidationFailed,
    InvalidStateTransition,
    AlreadyOnHold,
}

public sealed record ApplyEmergencyHoldOutcome(
    ApplyEmergencyHoldStatus Status,
    ApplyEmergencyHoldResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

// ---------- AD19c — POST /admin/transactions/:id/release-hold ----------

public sealed record ReleaseEmergencyHoldRequest(
    EmergencyHoldReleaseAction Action,
    string? Note);

/// <summary>
/// Released response — covers both <c>RESUME</c> (status, releasedAt, action)
/// and <c>CANCEL</c> (status=CANCELLED_ADMIN, releasedAt, action, itemReturned,
/// paymentRefunded). The CANCEL-only fields are nullable in the DTO so the
/// controller can serialise the RESUME shape without "ItemReturned": false
/// noise on the wire (07 §9.22).
/// </summary>
public sealed record ReleaseEmergencyHoldResponse(
    TransactionStatus Status,
    DateTime ReleasedAt,
    EmergencyHoldReleaseAction Action,
    bool? ItemReturned,
    bool? PaymentRefunded);

public enum ReleaseEmergencyHoldStatus
{
    Released,
    NotFound,
    ValidationFailed,
    NotOnHold,
    CannotCancelDeliveredHold,
}

public sealed record ReleaseEmergencyHoldOutcome(
    ReleaseEmergencyHoldStatus Status,
    ReleaseEmergencyHoldResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

// ---------- AD19d — POST /admin/transactions/hold-by-user/:userId ----------

/// <summary>
/// Bulk emergency hold over every active transaction of a single user — backs
/// the 04 §8.3 account-flag "Hold" action (03 §8.8). Reuses the same per-tx
/// freeze + state-machine + audit + outbox sequence as AD19b.
/// </summary>
public sealed record HoldUserTransactionsRequest(string? Reason);

/// <summary>
/// Result of a bulk hold. <see cref="HeldCount"/> is the number of transactions
/// transitioned to EMERGENCY_HOLD on this call (already-held transactions are
/// skipped, so the call is idempotent — a re-run returns 0).
/// </summary>
public sealed record HoldUserTransactionsResponse(
    int HeldCount,
    DateTime AppliedAt,
    IReadOnlyList<Guid> HeldTransactionIds);

public enum HoldUserTransactionsStatus
{
    Applied,
    ValidationFailed,
}

public sealed record HoldUserTransactionsOutcome(
    HoldUserTransactionsStatus Status,
    HoldUserTransactionsResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);
