using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Outbox;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;

namespace Skinora.API.Outbox;

/// <summary>
/// Default <see cref="IExternalIdempotencyService"/> — backed by the
/// <c>ExternalIdempotencyRecords</c> table (06 §3.21, 05 §5.1).
/// </summary>
/// <remarks>
/// All status transitions go through atomic conditional updates
/// (<see cref="EntityFrameworkQueryableExtensions.ExecuteUpdateAsync"/>) so
/// concurrent callers cannot stomp on each other. The initial INSERT relies
/// on the <c>UNIQUE(ServiceName, IdempotencyKey)</c> constraint to break
/// races; the loser of an insert race re-reads the row and falls into the
/// existing-record branch (06 §3.21 "concurrency acquisition kuralı").
/// </remarks>
public class ExternalIdempotencyService : IExternalIdempotencyService
{
    private const int MaxAcquireAttempts = 5;

    private readonly AppDbContext _dbContext;

    public ExternalIdempotencyService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ExternalIdempotencyAcquisition> AcquireAsync(
        string serviceName,
        string idempotencyKey,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceName);
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), leaseDuration, "Lease duration must be positive.");

        for (var attempt = 0; attempt < MaxAcquireAttempts; attempt++)
        {
            var existing = await _dbContext.ExternalIdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.ServiceName == serviceName && r.IdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (existing is null)
            {
                // No row yet — try to be the first writer. The UNIQUE
                // constraint breaks insert races; the loser falls through to
                // the next iteration and processes the now-visible row.
                _dbContext.ExternalIdempotencyRecords.Add(new ExternalIdempotencyRecord
                {
                    ServiceName = serviceName,
                    IdempotencyKey = idempotencyKey,
                    Status = ExternalIdempotencyStatus.in_progress,
                    LeaseExpiresAt = DateTime.UtcNow.Add(leaseDuration),
                    CreatedAt = DateTime.UtcNow,
                });

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return new ExternalIdempotencyAcquisition.Acquired();
                }
                catch (DbUpdateException)
                {
                    // Race lost — discard the unsaved Add and re-read.
                    _dbContext.ChangeTracker.Clear();
                    continue;
                }
            }

            switch (existing.Status)
            {
                case ExternalIdempotencyStatus.completed:
                    return new ExternalIdempotencyAcquisition.Replay(existing.ResultPayload);

                case ExternalIdempotencyStatus.failed:
                {
                    var newLease = DateTime.UtcNow.Add(leaseDuration);
                    var rows = await _dbContext.ExternalIdempotencyRecords
                        .Where(r => r.Id == existing.Id
                                    && r.Status == ExternalIdempotencyStatus.failed)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(r => r.Status, ExternalIdempotencyStatus.in_progress)
                                .SetProperty(r => r.LeaseExpiresAt, (DateTime?)newLease),
                            cancellationToken);

                    if (rows == 1)
                        return new ExternalIdempotencyAcquisition.Acquired();

                    // Lost the race — another caller claimed it. Re-read.
                    continue;
                }

                case ExternalIdempotencyStatus.in_progress:
                {
                    var now = DateTime.UtcNow;
                    var leaseValid = existing.LeaseExpiresAt.HasValue
                                     && existing.LeaseExpiresAt.Value > now;

                    if (leaseValid)
                        return new ExternalIdempotencyAcquisition.Blocked();

                    // Stale lease — atomic reclaim from in_progress to failed
                    // (06 §3.21 stale recovery). The loop then re-reads and
                    // either claims the now-failed row or sees that another
                    // caller already did.
                    await _dbContext.ExternalIdempotencyRecords
                        .Where(r => r.Id == existing.Id
                                    && r.Status == ExternalIdempotencyStatus.in_progress
                                    && r.LeaseExpiresAt != null
                                    && r.LeaseExpiresAt < now)
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(r => r.Status, ExternalIdempotencyStatus.failed)
                                .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null),
                            cancellationToken);

                    continue;
                }
            }
        }

        throw new InvalidOperationException(
            $"Could not acquire external idempotency lease for ({serviceName}, {idempotencyKey}) " +
            $"after {MaxAcquireAttempts} attempts — concurrent contention exceeded retry budget.");
    }

    public async Task CompleteAsync(
        string serviceName,
        string idempotencyKey,
        string? resultPayload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceName);
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);

        var now = (DateTime?)DateTime.UtcNow;
        var rows = await _dbContext.ExternalIdempotencyRecords
            .Where(r => r.ServiceName == serviceName
                        && r.IdempotencyKey == idempotencyKey
                        && r.Status == ExternalIdempotencyStatus.in_progress)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, ExternalIdempotencyStatus.completed)
                    .SetProperty(r => r.CompletedAt, now)
                    .SetProperty(r => r.ResultPayload, resultPayload)
                    .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null),
                cancellationToken);

        if (rows == 0)
        {
            throw new InvalidOperationException(
                $"Cannot complete external idempotency record ({serviceName}, {idempotencyKey}) — " +
                "no in_progress row found. Caller must Acquire before Complete.");
        }
    }

    public async Task FailAsync(
        string serviceName,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceName);
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);

        var rows = await _dbContext.ExternalIdempotencyRecords
            .Where(r => r.ServiceName == serviceName
                        && r.IdempotencyKey == idempotencyKey
                        && r.Status == ExternalIdempotencyStatus.in_progress)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, ExternalIdempotencyStatus.failed)
                    .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null)
                    .SetProperty(r => r.CompletedAt, (DateTime?)null),
                cancellationToken);

        if (rows == 0)
        {
            throw new InvalidOperationException(
                $"Cannot fail external idempotency record ({serviceName}, {idempotencyKey}) — " +
                "no in_progress row found. Caller must Acquire before Fail.");
        }
    }
}
