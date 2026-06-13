using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Emitted by the trade-offer dispatch engine (T106a) when the Steam sidecar
/// reports a definitive <c>failed</c> for a trade-offer send (retries exhausted
/// or a permanent eResult / item rejection). The transaction is left in place
/// (a FAILED <c>TradeOffer</c> row blocks re-dispatch) and the timeout pipeline
/// cancels it; this event raises an admin alert through the notification
/// pipeline (mirrors <see cref="TransferDispatchFailedEvent"/> for outbound
/// transfers).
/// </summary>
/// <remarks>
/// Transient failures (sidecar unreachable / 5xx / timeout) do NOT emit this
/// event — they are retried on the next dispatch tick. Only a terminal sidecar
/// <c>failed</c> response surfaces here.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">Transaction whose dispatch failed.</param>
/// <param name="Direction">TO_SELLER (escrow) / TO_BUYER (delivery) / RETURN_TO_SELLER (refund).</param>
/// <param name="PlatformSteamBotId">Bot the offer was dispatched through (Guid.Empty if unknown).</param>
/// <param name="LastErrorReason">Sidecar failure reason from the final attempt.</param>
/// <param name="Attempts">Sidecar send attempts performed before giving up.</param>
/// <param name="OccurredAt">UTC timestamp the event was committed.</param>
public record TradeOfferDispatchFailedEvent(
    Guid EventId,
    Guid TransactionId,
    TradeOfferDirection Direction,
    Guid PlatformSteamBotId,
    string? LastErrorReason,
    int Attempts,
    DateTime OccurredAt) : IDomainEvent;
