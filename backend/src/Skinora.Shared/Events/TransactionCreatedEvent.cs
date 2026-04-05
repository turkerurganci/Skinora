using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

public record TransactionCreatedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid SellerId,
    Guid? BuyerId,
    string ItemName,
    decimal Price,
    StablecoinType Stablecoin,
    DateTime OccurredAt) : IDomainEvent;
