using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Outbox;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;

namespace Skinora.API.Outbox;

/// <summary>
/// Default <see cref="IProcessedEventStore"/> — backed by the
/// <c>ProcessedEvents</c> table (06 §3.19, 09 §9.3).
/// </summary>
/// <remarks>
/// <see cref="MarkAsProcessedAsync"/> deliberately does not call
/// <c>SaveChangesAsync</c>: the consumer's side-effect changes and the
/// <c>ProcessedEvent</c> row must commit in the same transaction or neither
/// at all (09 §9.3). The unique index on <c>(EventId, ConsumerName)</c>
/// is the last line of defence against concurrent duplicates.
/// </remarks>
public class ProcessedEventStore : IProcessedEventStore
{
    private readonly AppDbContext _dbContext;

    public ProcessedEventStore(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(
        Guid eventId,
        string consumerName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(consumerName);

        return _dbContext.ProcessedEvents
            .AsNoTracking()
            .AnyAsync(p => p.EventId == eventId && p.ConsumerName == consumerName, cancellationToken);
    }

    public Task MarkAsProcessedAsync(
        Guid eventId,
        string consumerName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(consumerName);

        _dbContext.ProcessedEvents.Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            ConsumerName = consumerName,
            ProcessedAt = DateTime.UtcNow,
        });
        return Task.CompletedTask;
    }
}
