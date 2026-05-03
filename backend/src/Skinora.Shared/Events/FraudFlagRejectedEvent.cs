using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted after an admin rejects a fraud flag (T54 — 07 §9.5, 03 §8.2).
/// For transaction pre-create flags the linked transaction has just been
/// transitioned from <c>FLAGGED</c> to <c>CANCELLED_ADMIN</c>; the
/// notification fan-out tells both parties the transaction was cancelled.
/// For account flags the rejection records that the flag was a false
/// positive (no further effect on the account because the block was already
/// blocking — admin may follow up with separate AD endpoints to unblock).
/// </summary>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="FraudFlagId">Identifier of the reviewed <c>FraudFlag</c> row.</param>
/// <param name="UserId">User the flag was attached to.</param>
/// <param name="TransactionId"><c>null</c> when scope is <c>ACCOUNT_LEVEL</c>.</param>
/// <param name="Scope">Scope of the flag.</param>
/// <param name="Type">Type of fraud signal.</param>
/// <param name="ReviewedByAdminId">Admin user that rejected the flag.</param>
/// <param name="OccurredAt">UTC timestamp the rejection committed.</param>
public record FraudFlagRejectedEvent(
    Guid EventId,
    Guid FraudFlagId,
    Guid UserId,
    Guid? TransactionId,
    FraudFlagScope Scope,
    FraudFlagType Type,
    Guid ReviewedByAdminId,
    DateTime OccurredAt) : IDomainEvent;
