using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted after an admin approves a fraud flag (T54 — 07 §9.4, 03 §8.2).
/// For transaction pre-create flags the linked transaction has just been
/// promoted from <c>FLAGGED</c> to <c>CREATED</c>; the notification fan-out
/// tells the seller "İşleminize devam edebilirsiniz". For account flags the
/// account-level block lifts.
/// </summary>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="FraudFlagId">Identifier of the reviewed <c>FraudFlag</c> row.</param>
/// <param name="UserId">User the flag was attached to.</param>
/// <param name="TransactionId"><c>null</c> when scope is <c>ACCOUNT_LEVEL</c>.</param>
/// <param name="Scope">Scope of the flag.</param>
/// <param name="Type">Type of fraud signal.</param>
/// <param name="ReviewedByAdminId">Admin user that approved the flag.</param>
/// <param name="OccurredAt">UTC timestamp the approval committed.</param>
public record FraudFlagApprovedEvent(
    Guid EventId,
    Guid FraudFlagId,
    Guid UserId,
    Guid? TransactionId,
    FraudFlagScope Scope,
    FraudFlagType Type,
    Guid ReviewedByAdminId,
    DateTime OccurredAt) : IDomainEvent;
