using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Outbox notification raised by the cancel pipeline (T49 timeout, T51
/// user-cancel, T59 admin-cancel) once <c>PaymentAddress.MonitoringStatus</c>
/// has been stamped to <c>POST_CANCEL_24H</c>. A backend dispatcher consumes
/// the event and calls the blockchain sidecar's
/// <c>POST /api/monitor/post-cancel-start</c> so the address enters
/// gradual-cadence monitoring (T75 — 08 §3.4).
/// </summary>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">The transaction that was just cancelled.</param>
/// <param name="PaymentAddressId">PaymentAddress row id (06 §3.7).</param>
/// <param name="Address">Tron deposit address — sidecar registry key.</param>
/// <param name="ExpectedToken">Stablecoin the address was billed for.</param>
/// <param name="ExpectedContractAddress">TRC-20 contract address resolved
/// from <c>ExpectedToken</c>; sidecar uses it for the phase 1 filter.</param>
/// <param name="CancelledAt">UTC moment the transaction left its active
/// state. Sidecar anchors the 24h/7d/30d windows here so a sidecar restart
/// resumes on the same boundary.</param>
/// <param name="OccurredAt">UTC timestamp the event was committed.</param>
public record PostCancelMonitorStartRequestedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid PaymentAddressId,
    string Address,
    StablecoinType ExpectedToken,
    string ExpectedContractAddress,
    DateTime CancelledAt,
    DateTime OccurredAt) : IDomainEvent;
