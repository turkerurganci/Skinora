using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Admin;

// ---------- AD19 — POST /admin/transactions/:id/cancel ----------

public sealed record AdminCancelTransactionRequest(string? Reason);

// ItemReturned dropped in v3.0 — the platform never holds the item, so a
// cancellation can only ever move money (02 §9).
public sealed record AdminCancelTransactionResponse(
    TransactionStatus Status,
    DateTime CancelledAt,
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
/// and <c>CANCEL</c> (status=CANCELLED_ADMIN, releasedAt, action,
/// paymentRefunded). <see cref="PaymentRefunded"/> is the one CANCEL-only field
/// and is nullable so the controller can serialise the RESUME shape without it
/// on the wire (07 §9.22).
/// </summary>
/// <remarks>
/// The doc used to describe an <c>itemReturned</c> field as well. That field
/// left the record in v3.0: the platform never takes custody of the item, so a
/// cancellation has no item to return (02 §9). The record was updated then and
/// this summary was not (T133b).
/// </remarks>
public sealed record ReleaseEmergencyHoldResponse(
    TransactionStatus Status,
    DateTime ReleasedAt,
    EmergencyHoldReleaseAction Action,
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

// ---------- AD32 — POST /admin/transactions/:id/clear-settlement ----------

/// <summary>
/// Closes an escalated settlement in the SELLER's favour (07 §9.22b, T129 fix
/// round). The one lever that exists for a settlement the check cannot finish
/// on its own — before this, an escalated transaction had no terminating path
/// at all unless the buyer happened to open a dispute (validator finding B1).
/// </summary>
public sealed record ClearSettlementRequest(string? Reason);

public sealed record ClearSettlementResponse(
    TransactionStatus Status,
    DateTime SettlementVerifiedAt,
    string EscalationReason);

public enum ClearSettlementStatus
{
    Cleared,
    NotFound,
    ValidationFailed,
    NotEscalated,
    AlreadyResolved,
    InvalidStateTransition,
}

public sealed record ClearSettlementOutcome(
    ClearSettlementStatus Status,
    ClearSettlementResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);
