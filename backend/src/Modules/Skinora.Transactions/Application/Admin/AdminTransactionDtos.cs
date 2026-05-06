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
