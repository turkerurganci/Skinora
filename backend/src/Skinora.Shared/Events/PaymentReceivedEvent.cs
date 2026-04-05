using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

public record PaymentReceivedEvent(
    Guid EventId,
    Guid TransactionId,
    decimal Amount,
    StablecoinType Stablecoin,
    string TxHash,
    DateTime OccurredAt) : IDomainEvent;
