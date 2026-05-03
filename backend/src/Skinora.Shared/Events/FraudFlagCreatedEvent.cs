using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by <c>FraudFlagService</c> after a fraud flag row is created
/// (T54 — 02 §14.0, 03 §7, 07 §9.2). The Notifications consumer is the
/// admin alert channel: the flag goes into the admin review queue so the
/// admin sees a "new flag" badge / notification.
/// </summary>
/// <remarks>
/// The party-side notification (seller/buyer that the transaction was
/// flagged) is forward-deferred to the dedicated approve/reject events —
/// at flag creation the parties are simply told the transaction is under
/// review via the FLAGGED status surfaced by 07 §7.4 transaction detail.
/// Account-level flags do not have transaction parties.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="FraudFlagId">Identifier of the new <c>FraudFlag</c> row.</param>
/// <param name="UserId">User the flag is attached to (seller for tx flags, account holder for account flags).</param>
/// <param name="TransactionId"><c>null</c> when scope is <c>ACCOUNT_LEVEL</c>.</param>
/// <param name="Scope">Scope of the flag (account vs. transaction pre-create).</param>
/// <param name="Type">Type of fraud signal that produced the flag.</param>
/// <param name="EmergencyHoldAppliedToActiveTransactions">
/// <c>true</c> when account-level cascade applied <c>EMERGENCY_HOLD</c> to
/// the user's active transactions (02 §14.0 high-risk path). Notification
/// templates use this to add the "active transactions frozen" line.
/// </param>
/// <param name="OccurredAt">UTC timestamp at which the flag was committed.</param>
public record FraudFlagCreatedEvent(
    Guid EventId,
    Guid FraudFlagId,
    Guid UserId,
    Guid? TransactionId,
    FraudFlagScope Scope,
    FraudFlagType Type,
    bool EmergencyHoldAppliedToActiveTransactions,
    DateTime OccurredAt) : IDomainEvent;
