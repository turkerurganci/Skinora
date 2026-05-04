using Skinora.Shared.Enums;

namespace Skinora.Fraud.Application.Flags;

/// <summary>
/// Central fraud-flag write port (T54 — 02 §14.0, 03 §7–§8.2, 07 §9.4–§9.5).
/// Signal generators (T55–T57 AML, T82 sanctions, future account-takeover
/// detector) call into <see cref="StageAccountFlagAsync"/> /
/// <see cref="StageTransactionFlagAsync"/>; admin endpoints AD4 / AD5 call
/// <see cref="ApproveAsync"/> / <see cref="RejectAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomicity boundary (09 §13.3):</b> the staging methods only
/// <c>Add</c> the entity (and outbox row + audit log + cascade
/// <c>EMERGENCY_HOLD</c>) on the change tracker; the caller owns the
/// surrounding <c>SaveChangesAsync</c>. This mirrors <see cref="Skinora.Platform.Application.Audit.IAuditLogger"/>
/// and lets <c>TransactionCreationService</c> insert the transaction row
/// + the matching pre-create flag in a single SaveChanges so an admin
/// can never observe a <c>FLAGGED</c> transaction without its flag row.
/// </para>
/// <para>
/// <b>Approve / Reject:</b> these own their own <c>SaveChanges</c> because
/// they are top-level commands with no caller-owned transaction.
/// </para>
/// </remarks>
public interface IFraudFlagService
{
    /// <summary>
    /// Stage an <see cref="FraudFlagScope.ACCOUNT_LEVEL"/> flag on the change
    /// tracker, schedule the optional <c>EMERGENCY_HOLD</c> cascade and
    /// the <c>FRAUD_FLAG_CREATED</c> + (per-tx) <c>FRAUD_FLAG_AUTO_HOLD</c>
    /// audit rows. Caller must call <c>SaveChangesAsync</c>.
    /// </summary>
    /// <param name="userId">User to be flagged.</param>
    /// <param name="type">Type of fraud signal that produced the flag.</param>
    /// <param name="details">Free-form JSON payload (07 §9.3 <c>flagDetail</c>).</param>
    /// <param name="actorId">Caller acting on behalf of (admin guid or <see cref="Skinora.Shared.Domain.Seed.SeedConstants.SystemUserId"/>).</param>
    /// <param name="actorType">Admin → <see cref="ActorType.ADMIN"/>; automated detection → <see cref="ActorType.SYSTEM"/>.</param>
    /// <param name="cascadeEmergencyHold">
    /// When <c>true</c>, every active transaction belonging to <paramref name="userId"/>
    /// is transitioned to <c>EMERGENCY_HOLD</c> with reason <paramref name="emergencyHoldReason"/>.
    /// Reserved for high-risk types (sanctions match, account takeover) per 02 §14.0.
    /// </param>
    /// <param name="emergencyHoldReason">Reason text propagated to <c>Transaction.EmergencyHoldReason</c> when cascading.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The freshly assigned <c>FraudFlag.Id</c>.</returns>
    Task<Guid> StageAccountFlagAsync(
        Guid userId,
        FraudFlagType type,
        string details,
        Guid actorId,
        ActorType actorType,
        bool cascadeEmergencyHold,
        string? emergencyHoldReason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stage a <see cref="FraudFlagScope.TRANSACTION_PRE_CREATE"/> flag on the
    /// change tracker. Caller (<c>TransactionCreationService</c>) must already
    /// have set <c>Transaction.Status = FLAGGED</c> and added the entity; this
    /// service appends the matching <c>FraudFlag</c> row.
    /// </summary>
    /// <param name="userId">Seller user id (the seller initiates the transaction).</param>
    /// <param name="transactionId">Transaction the flag is attached to.</param>
    /// <param name="type">Type of fraud signal.</param>
    /// <param name="details">Free-form JSON payload (07 §9.3 <c>flagDetail</c>).</param>
    /// <param name="actorId">Actor guid (typically <see cref="Skinora.Shared.Domain.Seed.SeedConstants.SystemUserId"/> for automated checks).</param>
    /// <param name="actorType">Always <see cref="ActorType.SYSTEM"/> for the pre-create path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The freshly assigned <c>FraudFlag.Id</c>.</returns>
    Task<Guid> StageTransactionFlagAsync(
        Guid userId,
        Guid transactionId,
        FraudFlagType type,
        string details,
        Guid actorId,
        ActorType actorType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Approve a flag (07 §9.4). For transaction pre-create flags the linked
    /// transaction is promoted from <c>FLAGGED</c> to <c>CREATED</c> using
    /// <see cref="Skinora.Transactions.Domain.StateMachine.TransactionTrigger.AdminApprove"/>
    /// and an <c>AcceptDeadline</c> is initialised. Owns its own
    /// <c>SaveChanges</c> + DB transaction.
    /// </summary>
    Task<ApproveFlagOutcome> ApproveAsync(
        Guid flagId, Guid adminId, string? note, CancellationToken cancellationToken);

    /// <summary>
    /// Reject a flag (07 §9.5). For transaction pre-create flags the linked
    /// transaction is transitioned to <c>CANCELLED_ADMIN</c> via
    /// <see cref="Skinora.Transactions.Domain.StateMachine.TransactionTrigger.AdminReject"/>.
    /// Owns its own <c>SaveChanges</c> + DB transaction.
    /// </summary>
    Task<RejectFlagOutcome> RejectAsync(
        Guid flagId, Guid adminId, string? note, CancellationToken cancellationToken);
}
