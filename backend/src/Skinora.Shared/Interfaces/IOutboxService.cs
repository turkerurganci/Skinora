using Skinora.Shared.Domain;

namespace Skinora.Shared.Interfaces;

public interface IOutboxService
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
