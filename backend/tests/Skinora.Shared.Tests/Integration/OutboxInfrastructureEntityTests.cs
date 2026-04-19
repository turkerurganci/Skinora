using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;

namespace Skinora.Shared.Tests.Integration;

/// <summary>
/// DB-level integration tests for outbox infrastructure entities
/// (T25 acceptance criteria — 06 §3.18, §3.19, §3.21).
/// Focused on CHECK constraints and unique indexes; behavioural tests live in
/// Skinora.API.Tests.
/// </summary>
public class OutboxInfrastructureEntityTests : IntegrationTestBase
{
    // ========== OutboxMessage CHECK ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OutboxMessage_Pending_With_ProcessedAt_Rejected()
    {
        // 06 §3.18: PENDING → ProcessedAt NULL
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "TestEvent",
            Payload = "{}",
            Status = OutboxMessageStatus.PENDING,
            CreatedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow
        };
        Context.Set<OutboxMessage>().Add(msg);

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OutboxMessage_Processed_Without_ProcessedAt_Rejected()
    {
        // 06 §3.18: PROCESSED → ProcessedAt NOT NULL
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "TestEvent",
            Payload = "{}",
            Status = OutboxMessageStatus.PROCESSED,
            CreatedAt = DateTime.UtcNow,
            ProcessedAt = null
        };
        Context.Set<OutboxMessage>().Add(msg);

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OutboxMessage_Failed_Without_ErrorMessage_Rejected()
    {
        // 06 §3.18: FAILED → ProcessedAt NULL and ErrorMessage NOT NULL
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "TestEvent",
            Payload = "{}",
            Status = OutboxMessageStatus.FAILED,
            CreatedAt = DateTime.UtcNow,
            ProcessedAt = null,
            ErrorMessage = null
        };
        Context.Set<OutboxMessage>().Add(msg);

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task OutboxMessage_Valid_Processed_RoundTrips()
    {
        var msg = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "TestEvent",
            Payload = "{\"a\": 1}",
            Status = OutboxMessageStatus.PROCESSED,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            ProcessedAt = DateTime.UtcNow
        };
        Context.Set<OutboxMessage>().Add(msg);
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var loaded = await readCtx.Set<OutboxMessage>().FirstAsync(m => m.Id == msg.Id);
        Assert.Equal(OutboxMessageStatus.PROCESSED, loaded.Status);
        Assert.NotNull(loaded.ProcessedAt);
    }

    // ========== ProcessedEvent Unique ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessedEvent_SameEvent_SameConsumer_Rejected()
    {
        // 06 §3.19: UNIQUE(EventId, ConsumerName)
        var eventId = Guid.NewGuid();
        var first = new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            ConsumerName = "TestConsumer",
            ProcessedAt = DateTime.UtcNow
        };
        Context.Set<ProcessedEvent>().Add(first);
        await Context.SaveChangesAsync();

        await using var ctx = CreateContext();
        var duplicate = new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            ConsumerName = "TestConsumer",
            ProcessedAt = DateTime.UtcNow
        };
        ctx.Set<ProcessedEvent>().Add(duplicate);

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessedEvent_SameEvent_DifferentConsumer_Allowed()
    {
        var eventId = Guid.NewGuid();
        Context.Set<ProcessedEvent>().Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            ConsumerName = "ConsumerA",
            ProcessedAt = DateTime.UtcNow
        });
        Context.Set<ProcessedEvent>().Add(new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            ConsumerName = "ConsumerB",
            ProcessedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var count = await readCtx.Set<ProcessedEvent>()
            .Where(p => p.EventId == eventId)
            .CountAsync();
        Assert.Equal(2, count);
    }

    // ========== ExternalIdempotencyRecord ==========

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExternalIdempotency_InProgress_Requires_Lease()
    {
        // 06 §3.21: in_progress → LeaseExpiresAt NOT NULL
        var rec = new ExternalIdempotencyRecord
        {
            IdempotencyKey = Guid.NewGuid().ToString(),
            ServiceName = "SteamSidecar",
            Status = ExternalIdempotencyStatus.in_progress,
            LeaseExpiresAt = null,
            CreatedAt = DateTime.UtcNow
        };
        Context.Set<ExternalIdempotencyRecord>().Add(rec);

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExternalIdempotency_Completed_Requires_CompletedAt()
    {
        // 06 §3.21: completed → CompletedAt NOT NULL
        var rec = new ExternalIdempotencyRecord
        {
            IdempotencyKey = Guid.NewGuid().ToString(),
            ServiceName = "SteamSidecar",
            Status = ExternalIdempotencyStatus.completed,
            CompletedAt = null,
            CreatedAt = DateTime.UtcNow
        };
        Context.Set<ExternalIdempotencyRecord>().Add(rec);

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExternalIdempotency_SameKey_DifferentService_Allowed()
    {
        var key = Guid.NewGuid().ToString();
        Context.Set<ExternalIdempotencyRecord>().Add(new ExternalIdempotencyRecord
        {
            IdempotencyKey = key,
            ServiceName = "SteamSidecar",
            Status = ExternalIdempotencyStatus.in_progress,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        });
        Context.Set<ExternalIdempotencyRecord>().Add(new ExternalIdempotencyRecord
        {
            IdempotencyKey = key,
            ServiceName = "BlockchainService",
            Status = ExternalIdempotencyStatus.in_progress,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        await using var readCtx = CreateContext();
        var count = await readCtx.Set<ExternalIdempotencyRecord>()
            .Where(r => r.IdempotencyKey == key)
            .CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExternalIdempotency_SameService_SameKey_Rejected()
    {
        var key = Guid.NewGuid().ToString();
        Context.Set<ExternalIdempotencyRecord>().Add(new ExternalIdempotencyRecord
        {
            IdempotencyKey = key,
            ServiceName = "SteamSidecar",
            Status = ExternalIdempotencyStatus.in_progress,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        await using var ctx = CreateContext();
        ctx.Set<ExternalIdempotencyRecord>().Add(new ExternalIdempotencyRecord
        {
            IdempotencyKey = key,
            ServiceName = "SteamSidecar",
            Status = ExternalIdempotencyStatus.in_progress,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
