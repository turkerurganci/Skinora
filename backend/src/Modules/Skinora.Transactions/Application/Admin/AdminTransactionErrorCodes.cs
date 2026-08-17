namespace Skinora.Transactions.Application.Admin;

/// <summary>
/// Stable error codes returned by the admin transaction lifecycle endpoints
/// (T59 — 07 §9.20–§9.22). Mirrors the
/// <see cref="Skinora.Transactions.Application.Lifecycle.TransactionErrorCodes"/>
/// pattern so the controller can build envelopes without coupling to service
/// internals.
/// </summary>
public static class AdminTransactionErrorCodes
{
    public const string TransactionNotFound = "TRANSACTION_NOT_FOUND";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string InvalidStateTransition = "INVALID_STATE_TRANSITION";
    public const string CannotCancelAtDeliveryStage = "CANNOT_CANCEL_AT_DELIVERY_STAGE";
    public const string CannotCancelDeliveredHold = "CANNOT_CANCEL_DELIVERED_HOLD";
    public const string AlreadyOnHold = "ALREADY_ON_HOLD";
    public const string NotOnHold = "NOT_ON_HOLD";

    // AD32 — settlement clearance (T129 fix round, 07 §9.22b).
    public const string SettlementNotEscalated = "SETTLEMENT_NOT_ESCALATED";
    public const string SettlementAlreadyResolved = "SETTLEMENT_ALREADY_RESOLVED";
}
