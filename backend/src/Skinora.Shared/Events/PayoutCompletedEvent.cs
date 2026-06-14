using Skinora.Shared.Domain;

namespace Skinora.Shared.Events;

/// <summary>
/// Raised by <c>OutgoingTransferConfirmationJob</c> when a SELLER_PAYOUT
/// <c>BlockchainTransaction</c> row reaches on-chain finality (CONFIRMED,
/// ≥20 blocks — 08 §3.4). The seller has now been paid; the WP1 completion
/// consumer fires <c>TransactionTrigger.Complete</c> to move the transaction
/// to COMPLETED (03 §2.4 step 6). Carries the broadcast tx hash and net
/// payout amount so downstream notification / realtime consumers can surface
/// "Ödemeniz gönderildi" without re-querying the chain row.
/// </summary>
public record PayoutCompletedEvent(
    Guid EventId,
    Guid TransactionId,
    string PayoutTxHash,
    decimal NetAmount,
    DateTime OccurredAt) : IDomainEvent;
