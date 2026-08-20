using Skinora.Shared.Domain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Events;

/// <summary>
/// Outbox notification raised by the <c>ACCEPTED → SELLER_CONFIRMED</c>
/// transition (T123) once the buyer's payment window is armed. A backend
/// dispatcher consumes the event and calls the blockchain sidecar's
/// <c>POST /api/monitor/start</c> so the deposit address enters active
/// 3-second polling (T71 — 08 §3.4).
/// </summary>
/// <remarks>
/// Published inside the same <c>SaveChangesAsync</c> as the transition
/// (09 §13.3): a rolled-back confirmation must not leave a monitor armed on
/// an address the buyer was never told about. Delivery is at-least-once and
/// the sidecar's <c>start</c> is idempotent per address, so a duplicate
/// dispatch is a no-op that preserves the existing cursor/dedup state.
/// </remarks>
/// <param name="EventId">Outbox-level event identifier.</param>
/// <param name="TransactionId">The transaction whose payment window opened.</param>
/// <param name="PaymentAddressId">PaymentAddress row id (06 §3.7).</param>
/// <param name="Address">Tron deposit address — sidecar registry key.</param>
/// <param name="ExpectedToken">Stablecoin the address was billed for.</param>
/// <param name="ExpectedContractAddress">TRC-20 contract address resolved from
/// <paramref name="ExpectedToken"/>; the sidecar uses it for the 08 §3.4
/// phase 1 filter.</param>
/// <param name="OccurredAt">UTC timestamp the event was committed.</param>
public record PaymentMonitorStartRequestedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid PaymentAddressId,
    string Address,
    StablecoinType ExpectedToken,
    string ExpectedContractAddress,
    DateTime OccurredAt) : IDomainEvent;
