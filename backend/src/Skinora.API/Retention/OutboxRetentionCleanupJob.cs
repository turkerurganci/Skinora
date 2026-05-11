using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;

namespace Skinora.API.Retention;

/// <summary>
/// Recurring hard-delete sweep for the three retention-based outbox tables —
/// task T63b acceptance criterion "OutboxMessage + ProcessedEvent +
/// ExternalIdempotencyRecord 30 gün sonra toplu hard delete" (06 §1, §3.18,
/// §3.19, §3.21).
/// </summary>
/// <remarks>
/// <para>
/// Delete order is fixed: ProcessedEvent → OutboxMessage → ExternalIdempotencyRecord.
/// The first two share an event identity but no DB-level FK (06 §3.19) so the
/// sweep enforces the ordering in application code: a consumer-side marker
/// must be gone before its producer row is purged. ExternalIdempotencyRecord
/// is independent and runs last for symmetry.
/// </para>
/// <para>
/// Each table is purged in bounded batches; the loop exits once a batch finds
/// no eligible rows. Batch size and retention window are read from SystemSettings
/// on every run so admins can tune behaviour without redeploying.
/// </para>
/// <para>
/// Eligibility:
/// <list type="bullet">
///   <item>ProcessedEvent — <see cref="ProcessedEvent.ProcessedAt"/> &lt; threshold (every row is by definition processed).</item>
///   <item>OutboxMessage — <see cref="OutboxMessageStatus.PROCESSED"/> AND <see cref="OutboxMessage.ProcessedAt"/> &lt; threshold.
///       PENDING/FAILED rows stay because the dispatcher may still retry them (06 §3.18 retry semantiği).</item>
///   <item>ExternalIdempotencyRecord — <see cref="ExternalIdempotencyStatus.completed"/> AND
///       <see cref="ExternalIdempotencyRecord.CompletedAt"/> &lt; threshold.
///       in_progress / failed rows stay because §3.21 lease/retry can still claim them.</item>
/// </list>
/// </para>
/// <para>
/// Hangfire expression serialization constrains the handler signature to
/// <c>Expression&lt;Action&lt;T&gt;&gt;</c>, so <see cref="Execute"/> blocks on
/// the async core. The job runs once a day so tying up a worker thread for
/// the duration of a sweep is acceptable.
/// </para>
/// </remarks>
public sealed class OutboxRetentionCleanupJob
{
    public const string RecurringJobId = "outbox-retention-cleanup";

    public const string OutboxRetentionDaysKey = "retention.outbox_message_days";
    public const string ProcessedEventRetentionDaysKey = "retention.processed_event_days";
    public const string ExternalIdempotencyRetentionDaysKey = "retention.external_idempotency_days";
    public const string BatchSizeKey = "retention.batch_size_outbox";

    public const int DefaultRetentionDays = 30;
    public const int DefaultBatchSize = 1000;

    private readonly AppDbContext _db;
    private readonly ILogger<OutboxRetentionCleanupJob> _logger;

    public OutboxRetentionCleanupJob(AppDbContext db, ILogger<OutboxRetentionCleanupJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    public void Execute() => ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();

    public async Task<OutboxRetentionSweepResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var batchSize = await ReadSettingAsync(BatchSizeKey, DefaultBatchSize, cancellationToken);
        var now = DateTime.UtcNow;

        var processedEventDays = await ReadSettingAsync(
            ProcessedEventRetentionDaysKey, DefaultRetentionDays, cancellationToken);
        var processedEventDeleted = await PurgeProcessedEventsAsync(
            now - TimeSpan.FromDays(processedEventDays), batchSize, cancellationToken);

        var outboxDays = await ReadSettingAsync(
            OutboxRetentionDaysKey, DefaultRetentionDays, cancellationToken);
        var outboxDeleted = await PurgeOutboxMessagesAsync(
            now - TimeSpan.FromDays(outboxDays), batchSize, cancellationToken);

        var idempotencyDays = await ReadSettingAsync(
            ExternalIdempotencyRetentionDaysKey, DefaultRetentionDays, cancellationToken);
        var idempotencyDeleted = await PurgeExternalIdempotencyAsync(
            now - TimeSpan.FromDays(idempotencyDays), batchSize, cancellationToken);

        var result = new OutboxRetentionSweepResult(
            processedEventDeleted, outboxDeleted, idempotencyDeleted);

        if (result.TotalDeleted > 0)
        {
            _logger.LogInformation(
                "OutboxRetentionCleanupJob purged ProcessedEvent={ProcessedEvent}, OutboxMessage={OutboxMessage}, ExternalIdempotencyRecord={Idempotency} row(s).",
                processedEventDeleted, outboxDeleted, idempotencyDeleted);
        }
        else
        {
            _logger.LogDebug(
                "OutboxRetentionCleanupJob found no eligible rows (cutoff windows: ProcessedEvent={ProcessedEventDays}d, OutboxMessage={OutboxDays}d, ExternalIdempotency={IdempotencyDays}d).",
                processedEventDays, outboxDays, idempotencyDays);
        }

        return result;
    }

    private async Task<int> PurgeProcessedEventsAsync(
        DateTime threshold, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ids = await _db.Set<ProcessedEvent>()
                .Where(e => e.ProcessedAt < threshold)
                .OrderBy(e => e.ProcessedAt)
                .Take(batchSize)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0) break;

            var deleted = await _db.Set<ProcessedEvent>()
                .Where(e => ids.Contains(e.Id))
                .ExecuteDeleteAsync(cancellationToken);

            total += deleted;
            if (deleted < batchSize) break;
        }
        return total;
    }

    private async Task<int> PurgeOutboxMessagesAsync(
        DateTime threshold, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ids = await _db.Set<OutboxMessage>()
                .Where(m => m.Status == OutboxMessageStatus.PROCESSED
                            && m.ProcessedAt != null
                            && m.ProcessedAt < threshold)
                .OrderBy(m => m.ProcessedAt)
                .Take(batchSize)
                .Select(m => m.Id)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0) break;

            var deleted = await _db.Set<OutboxMessage>()
                .Where(m => ids.Contains(m.Id))
                .ExecuteDeleteAsync(cancellationToken);

            total += deleted;
            if (deleted < batchSize) break;
        }
        return total;
    }

    private async Task<int> PurgeExternalIdempotencyAsync(
        DateTime threshold, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ids = await _db.Set<ExternalIdempotencyRecord>()
                .Where(r => r.Status == ExternalIdempotencyStatus.completed
                            && r.CompletedAt != null
                            && r.CompletedAt < threshold)
                .OrderBy(r => r.CompletedAt)
                .Take(batchSize)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0) break;

            var deleted = await _db.Set<ExternalIdempotencyRecord>()
                .Where(r => ids.Contains(r.Id))
                .ExecuteDeleteAsync(cancellationToken);

            total += deleted;
            if (deleted < batchSize) break;
        }
        return total;
    }

    private async Task<int> ReadSettingAsync(
        string key, int defaultValue, CancellationToken cancellationToken)
    {
        var raw = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => s.Key == key && s.IsConfigured)
            .Select(s => s.Value)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
               && parsed > 0
            ? parsed
            : defaultValue;
    }
}

/// <summary>Per-table delete counts emitted by a single sweep.</summary>
public sealed record OutboxRetentionSweepResult(
    int ProcessedEventDeleted,
    int OutboxMessageDeleted,
    int ExternalIdempotencyRecordDeleted)
{
    public int TotalDeleted =>
        ProcessedEventDeleted + OutboxMessageDeleted + ExternalIdempotencyRecordDeleted;
}
