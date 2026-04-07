using MediatR;

namespace Skinora.Shared.Domain;

/// <summary>
/// Marker for domain events produced by state transitions and dispatched via
/// the outbox pipeline (05 §5.1, 09 §9.3).
/// </summary>
/// <remarks>
/// Inherits <see cref="INotification"/> from MediatR.Contracts so concrete
/// events can be published through <c>IPublisher.Publish(domainEvent)</c>
/// without an extra wrapper. The MediatR runtime is registered in the API
/// host (T10 outbox dispatcher); modules pull only the contracts package.
/// </remarks>
public interface IDomainEvent : INotification
{
    /// <summary>
    /// Stable identifier shared with the outbox row and consumer-side
    /// <c>ProcessedEvent</c> entry — there is exactly one ID per event
    /// (09 §9.3 "Tek ID, tek otorite").
    /// </summary>
    Guid EventId { get; }

    /// <summary>UTC timestamp when the event was raised.</summary>
    DateTime OccurredAt { get; }
}
