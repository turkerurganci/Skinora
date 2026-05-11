using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skinora.API.Retention;
using Skinora.Platform.Domain.Entities;
using Skinora.Platform.Infrastructure.Persistence;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;
using Skinora.Shared.Tests.Integration;
using Skinora.Users.Infrastructure.Persistence;

namespace Skinora.API.Tests.Integration.Retention;

/// <summary>
/// T63b — OutboxRetentionCleanupJob coverage. Verifies that the sweep
/// purges only eligible rows from each of the three retention-based outbox
/// tables (06 §3.18 OutboxMessage, §3.19 ProcessedEvent, §3.21
/// ExternalIdempotencyRecord), preserves rows that can still be retried
/// (PENDING / FAILED outbox messages, in_progress / failed idempotency
/// records), respects the batch size knob and reads the retention window
/// from SystemSettings.
/// </summary>
public class OutboxRetentionCleanupJobTests : IntegrationTestBase
{
    static OutboxRetentionCleanupJobTests()
    {
        UsersModuleDbRegistration.RegisterUsersModule();
        PlatformModuleDbRegistration.RegisterPlatformModule();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Eligible_ProcessedEvents_Are_Hard_Deleted()
    {
        var now = DateTime.UtcNow;
        await SeedProcessedEventAsync(now.AddDays(-40), "ConsumerA");
        await SeedProcessedEventAsync(now.AddDays(-31), "ConsumerB");
        await SeedProcessedEventAsync(now.AddDays(-15), "ConsumerC"); // fresh — preserved

        var sut = NewJob();
        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, result.ProcessedEventDeleted);

        var remaining = await Context.Set<ProcessedEvent>().AsNoTracking().ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("ConsumerC", remaining[0].ConsumerName);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OutboxMessages_Only_Processed_Rows_Past_Threshold_Are_Deleted()
    {
        var now = DateTime.UtcNow;
        var staleProcessed = await SeedOutboxAsync(
            OutboxMessageStatus.PROCESSED, createdAt: now.AddDays(-45), processedAt: now.AddDays(-40));
        var freshProcessed = await SeedOutboxAsync(
            OutboxMessageStatus.PROCESSED, createdAt: now.AddDays(-10), processedAt: now.AddDays(-5));
        var stalePending = await SeedOutboxAsync(
            OutboxMessageStatus.PENDING, createdAt: now.AddDays(-45), processedAt: null);
        var staleFailed = await SeedOutboxAsync(
            OutboxMessageStatus.FAILED, createdAt: now.AddDays(-45), processedAt: null, errorMessage: "retry me");

        var sut = NewJob();
        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, result.OutboxMessageDeleted);

        var remaining = await Context.Set<OutboxMessage>().AsNoTracking().Select(m => m.Id).ToListAsync();
        Assert.DoesNotContain(staleProcessed, remaining);
        Assert.Contains(freshProcessed, remaining);
        Assert.Contains(stalePending, remaining);
        Assert.Contains(staleFailed, remaining);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExternalIdempotency_Only_Completed_Rows_Past_Threshold_Are_Deleted()
    {
        var now = DateTime.UtcNow;
        var staleCompleted = await SeedIdempotencyAsync(
            ExternalIdempotencyStatus.completed, createdAt: now.AddDays(-45), completedAt: now.AddDays(-40));
        var freshCompleted = await SeedIdempotencyAsync(
            ExternalIdempotencyStatus.completed, createdAt: now.AddDays(-10), completedAt: now.AddDays(-5));
        var staleInProgress = await SeedIdempotencyAsync(
            ExternalIdempotencyStatus.in_progress,
            createdAt: now.AddDays(-45), completedAt: null, leaseExpiresAt: now.AddMinutes(-5));
        var staleFailed = await SeedIdempotencyAsync(
            ExternalIdempotencyStatus.failed, createdAt: now.AddDays(-45), completedAt: null);

        var sut = NewJob();
        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, result.ExternalIdempotencyRecordDeleted);

        var remaining = await Context.Set<ExternalIdempotencyRecord>().AsNoTracking()
            .Select(r => r.Id).ToListAsync();
        Assert.DoesNotContain(staleCompleted, remaining);
        Assert.Contains(freshCompleted, remaining);
        Assert.Contains(staleInProgress, remaining);
        Assert.Contains(staleFailed, remaining);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Batch_Size_Loop_Drains_All_Eligible_Rows()
    {
        // 12 eligible ProcessedEvent rows + batch size = 5 → 3 iterations expected.
        var now = DateTime.UtcNow;
        for (var i = 0; i < 12; i++)
        {
            await SeedProcessedEventAsync(now.AddDays(-40 - i), $"BatchConsumer{i}");
        }

        await OverrideSettingAsync(OutboxRetentionCleanupJob.BatchSizeKey, "5");

        var sut = NewJob();
        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(12, result.ProcessedEventDeleted);
        Assert.Empty(await Context.Set<ProcessedEvent>().AsNoTracking().ToListAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SystemSetting_Override_Shortens_Retention_Window()
    {
        // 7 days old must survive the 30d default but be eligible under a 5d window.
        var now = DateTime.UtcNow;
        await SeedProcessedEventAsync(now.AddDays(-7), "ConsumerSeven");

        var sutDefault = NewJob();
        var defaultRun = await sutDefault.ExecuteAsync(CancellationToken.None);
        Assert.Equal(0, defaultRun.ProcessedEventDeleted);

        await OverrideSettingAsync(OutboxRetentionCleanupJob.ProcessedEventRetentionDaysKey, "5");

        var sutShort = NewJob();
        var shortRun = await sutShort.ExecuteAsync(CancellationToken.None);
        Assert.Equal(1, shortRun.ProcessedEventDeleted);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task No_Eligible_Rows_Returns_Zero_Counts()
    {
        var sut = NewJob();
        var result = await sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(0, result.TotalDeleted);
    }

    private OutboxRetentionCleanupJob NewJob() =>
        new(Context, NullLogger<OutboxRetentionCleanupJob>.Instance);

    private async Task SeedProcessedEventAsync(DateTime processedAt, string consumerName)
    {
        Context.Set<ProcessedEvent>().Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            ConsumerName = consumerName,
            ProcessedAt = processedAt,
        });
        await Context.SaveChangesAsync();
    }

    private async Task<Guid> SeedOutboxAsync(
        OutboxMessageStatus status, DateTime createdAt, DateTime? processedAt, string? errorMessage = null)
    {
        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "TestEvent",
            Payload = "{}",
            Status = status,
            CreatedAt = createdAt,
            ProcessedAt = processedAt,
            ErrorMessage = errorMessage,
        };
        Context.Set<OutboxMessage>().Add(row);
        await Context.SaveChangesAsync();
        return row.Id;
    }

    private async Task<long> SeedIdempotencyAsync(
        ExternalIdempotencyStatus status,
        DateTime createdAt,
        DateTime? completedAt,
        DateTime? leaseExpiresAt = null)
    {
        var row = new ExternalIdempotencyRecord
        {
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            ServiceName = "TestService",
            Status = status,
            CreatedAt = createdAt,
            CompletedAt = completedAt,
            LeaseExpiresAt = leaseExpiresAt,
        };
        Context.Set<ExternalIdempotencyRecord>().Add(row);
        await Context.SaveChangesAsync();
        return row.Id;
    }

    private async Task OverrideSettingAsync(string key, string value)
    {
        var setting = await Context.Set<SystemSetting>().SingleAsync(s => s.Key == key);
        setting.Value = value;
        setting.IsConfigured = true;
        await Context.SaveChangesAsync();
    }
}
