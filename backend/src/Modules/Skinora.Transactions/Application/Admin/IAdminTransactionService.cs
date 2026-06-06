namespace Skinora.Transactions.Application.Admin;

/// <summary>
/// T59 admin transaction lifecycle orchestrator. Composes the
/// <see cref="Skinora.Transactions.Domain.StateMachine.TransactionStateMachine"/>
/// (T44) and
/// <see cref="Skinora.Transactions.Application.Timeouts.ITimeoutFreezeService"/>
/// (T50) with audit log + notification fan-out for the three admin endpoints
/// listed in 07 §9.20–§9.22.
/// </summary>
public interface IAdminTransactionService
{
    /// <summary>
    /// AD19 — direct admin cancel. Forbidden once the item has been delivered
    /// (returns <see cref="AdminCancelTransactionStatus.CannotCancelAtDeliveryStage"/>);
    /// rejects when the transaction is already terminal or under emergency hold.
    /// </summary>
    Task<AdminCancelTransactionOutcome> CancelAsync(
        Guid adminUserId,
        Guid transactionId,
        AdminCancelTransactionRequest request,
        string? ipAddress,
        CancellationToken cancellationToken);

    /// <summary>
    /// AD19b — apply emergency hold. The transaction stays in its pre-hold
    /// status (<c>PreviousStatusBeforeHold</c>) but every subsequent state
    /// machine trigger is rejected (05 §4.5) and timeout deadlines are frozen.
    /// </summary>
    Task<ApplyEmergencyHoldOutcome> ApplyEmergencyHoldAsync(
        Guid adminUserId,
        Guid transactionId,
        ApplyEmergencyHoldRequest request,
        string? ipAddress,
        CancellationToken cancellationToken);

    /// <summary>
    /// AD19c — release emergency hold. <c>RESUME</c> reschedules the timeout
    /// jobs and clears the freeze trio; <c>CANCEL</c> short-circuits to
    /// <c>CANCELLED_ADMIN</c> (forbidden when the pre-hold status was
    /// <c>ITEM_DELIVERED</c>, see 07 §9.22 + 03 §8.8).
    /// </summary>
    Task<ReleaseEmergencyHoldOutcome> ReleaseEmergencyHoldAsync(
        Guid adminUserId,
        Guid transactionId,
        ReleaseEmergencyHoldRequest request,
        string? ipAddress,
        CancellationToken cancellationToken);

    /// <summary>
    /// AD19d — apply emergency hold to every active transaction of
    /// <paramref name="targetUserId"/> (the user is either the seller or the
    /// buyer). Backs the 04 §8.3 account-flag "Hold" action (03 §8.8); reuses
    /// the AD19b per-transaction freeze + hold + notify sequence. Idempotent:
    /// already-held and terminal transactions are skipped.
    /// </summary>
    Task<HoldUserTransactionsOutcome> HoldAllUserTransactionsAsync(
        Guid adminUserId,
        Guid targetUserId,
        HoldUserTransactionsRequest request,
        string? ipAddress,
        CancellationToken cancellationToken);
}
