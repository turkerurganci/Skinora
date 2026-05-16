using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Webhooks;

namespace Skinora.API.Retention;

/// <summary>
/// Hard-purges expired <see cref="ProcessedNonce"/> rows (T68 — 05 §3.4,
/// 09 §11.3). Nonces stay long enough to cover the replay window plus a safety
/// margin (default 1h, configured via <c>WebhookSettings.NonceRetentionSeconds</c>);
/// once <c>ExpiresAt</c> is past, the row is no longer useful and is removed in
/// bounded batches.
/// </summary>
public sealed class ProcessedNonceCleanupJob
{
    public const string RecurringJobId = "processed-nonce-cleanup";

    private const int BatchSize = 5000;

    private readonly AppDbContext _db;
    private readonly ILogger<ProcessedNonceCleanupJob> _logger;

    public ProcessedNonceCleanupJob(AppDbContext db, ILogger<ProcessedNonceCleanupJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public void Execute() => ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var total = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ids = await _db.Set<ProcessedNonce>()
                .Where(n => n.ExpiresAt < now)
                .OrderBy(n => n.ExpiresAt)
                .Take(BatchSize)
                .Select(n => n.Id)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0)
            {
                break;
            }

            var deleted = await _db.Set<ProcessedNonce>()
                .Where(n => ids.Contains(n.Id))
                .ExecuteDeleteAsync(cancellationToken);

            total += deleted;
            if (deleted < BatchSize)
            {
                break;
            }
        }

        if (total > 0)
        {
            _logger.LogInformation("ProcessedNonceCleanupJob purged {Count} expired nonce row(s).", total);
        }
        else
        {
            _logger.LogDebug("ProcessedNonceCleanupJob found no expired rows.");
        }

        return total;
    }
}
