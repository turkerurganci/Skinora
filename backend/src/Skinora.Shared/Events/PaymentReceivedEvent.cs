using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// The buyer's payment reached its network's finality and was accepted.
/// </summary>
/// <param name="Network">The payment's network (T148). Defaults to TRON so an
/// outbox row written before the field existed still reads as TRON.</param>
/// <param name="ConfirmationCount">Confirmations recorded on the payment row.
/// <c>null</c> on rows written before T148; readers fall back to the network
/// policy (<c>BlockchainNetworkPolicy</c>) instead of a TRON constant.</param>
public record PaymentReceivedEvent(
    Guid EventId,
    Guid TransactionId,
    decimal Amount,
    StablecoinType Stablecoin,
    string TxHash,
    DateTime OccurredAt,
    BlockchainNetwork Network = BlockchainNetwork.TRON,
    int? ConfirmationCount = null) : IDomainEvent;
