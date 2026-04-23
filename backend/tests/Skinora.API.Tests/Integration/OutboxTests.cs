using System.Linq.Expressions;
using System.Text.Json;
using MediatR;
using Medallion.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skinora.API.Outbox;
using Skinora.API.Tests.Common;
using Skinora.Shared.BackgroundJobs;
using Skinora.Shared.Domain;
using Skinora.Shared.Enums;
using Skinora.Shared.Interfaces;
using Skinora.Shared.Outbox;
using Skinora.Shared.Persistence;
using Skinora.Shared.Persistence.Outbox;

namespace Skinora.API.Tests.Integration;

#region Test events & handlers

/// <summary>Round-trip test event used by the outbox integration tests.</summary>
public record TestOutboxEvent(
    Guid EventId,
    string Body,
    DateTime OccurredAt) : IDomainEvent;

/// <summary>Second test event used to prove dispatch is type-aware.</summary>
public record SecondTestOutboxEvent(
    Guid EventId,
    int Number,
    DateTime OccurredAt) : IDomainEvent;

/// <summary>Event whose handler always throws — used by failure-path tests.</summary>
public record AlwaysFailingTestEvent(
    Guid EventId,
    DateTime OccurredAt) : IDomainEvent;

public class TestOutboxEventHandler : INotificationHandler<TestOutboxEvent>
{
    public static List<TestOutboxEvent> Received { get; } = new();
    public static readonly object Sync = new();

    public Task Handle(TestOutboxEvent notification, CancellationToken cancellationToken)
    {
        lock (Sync) Received.Add(notification);
        return Task.CompletedTask;
    }

    public static void Reset() { lock (Sync) Received.Clear(); }
}

public class SecondTestOutboxEventHandler : INotificationHandler<SecondTestOutboxEvent>
{
    public static List<SecondTestOutboxEvent> Received { get; } = new();
    public static readonly object Sync = new();

    public Task Handle(SecondTestOutboxEvent notification, CancellationToken cancellationToken)
    {
        lock (Sync) Received.Add(notification);
        return Task.CompletedTask;
    }

    public static void Reset() { lock (Sync) Received.Clear(); }
}

public class AlwaysFailingTestEventHandler : INotificationHandler<AlwaysFailingTestEvent>
{
    public Task Handle(AlwaysFailingTestEvent notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("intentional test failure");
}

#endregion

#region Spy implementations

public class SpyJobScheduler : IBackgroundJobScheduler
{
    public List<TimeSpan> ScheduledDelays { get; } = new();
    public int EnqueueCount { get; private set; }
    public int DeleteCount { get; private set; }
    public int RecurringRegistrationCount { get; private set; }

    public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
    {
        ScheduledDelays.Add(delay);
        return Guid.NewGuid().ToString("N");
    }

    public string Enqueue<T>(Expression<Action<T>> methodCall)
    {
        EnqueueCount++;
        return Guid.NewGuid().ToString("N");
    }

    public bool Delete(string jobId)
    {
        DeleteCount++;
        return true;
    }

    public void AddOrUpdateRecurring<T>(
        string jobId, Expression<Action<T>> methodCall, string cronExpression)
    {
        RecurringRegistrationCount++;
    }
}

public class SpyOutboxAdminAlertSink : IOutboxAdminAlertSink
{
    public List<OutboxMessage> Raised { get; } = new();

    public Task RaiseMaxRetryExceededAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        Raised.Add(message);
        return Task.CompletedTask;
    }
}

#endregion

public class OutboxTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly OutboxOptions _options;

    public OutboxTests()
    {
        TestOutboxEventHandler.Reset();
        SecondTestOutboxEventHandler.Reset();

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();

        services.AddLogging();

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlite(_connection),
            ServiceLifetime.Scoped);

        // Outbox options tuned for fast tests.
        _options = new OutboxOptions
        {
            PollingIntervalSeconds = 1,
            BatchSize = 10,
            MaxRetryCount = 3,
            LockAcquireTimeoutSeconds = 0,
            DefaultExternalIdempotencyLeaseSeconds = 60,
        };
        services.AddSingleton(Options.Create(_options));

        // MediatR — register handlers from this test assembly.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(OutboxTests).Assembly));

        // In-memory distributed lock — same stub the API tests rely on.
        services.AddSingleton<IDistributedLockProvider, InMemoryDistributedLockProvider>();

        // Spies for scheduler and alert sink so tests can assert side effects.
        services.AddSingleton<SpyJobScheduler>();
        services.AddSingleton<IBackgroundJobScheduler>(sp => sp.GetRequiredService<SpyJobScheduler>());
        services.AddSingleton<SpyOutboxAdminAlertSink>();
        services.AddSingleton<IOutboxAdminAlertSink>(sp => sp.GetRequiredService<SpyOutboxAdminAlertSink>());

        // Outbox stack under test.
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<IProcessedEventStore, ProcessedEventStore>();
        services.AddScoped<IExternalIdempotencyService, ExternalIdempotencyService>();
        services.AddScoped<IOutboxDispatcher, OutboxDispatcher>();

        _serviceProvider = services.BuildServiceProvider();

        // Materialize the schema for the SQLite in-memory connection.
        using var bootstrapScope = _serviceProvider.CreateScope();
        var ctx = bootstrapScope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
        TestOutboxEventHandler.Reset();
        SecondTestOutboxEventHandler.Reset();
    }

    private async Task RunDispatcherAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>();
        await dispatcher.ProcessAndRescheduleAsync();
    }

    private async Task<Guid> PublishAsync(IDomainEvent @event)
    {
        using var scope = _serviceProvider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await outbox.PublishAsync(@event);
        await ctx.SaveChangesAsync();
        return @event.EventId;
    }

    private async Task<List<OutboxMessage>> AllOutboxAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await ctx.OutboxMessages.AsNoTracking().OrderBy(m => m.CreatedAt).ToListAsync();
    }

    // ----------------------------- Producer atomicity ------------------

    [Fact]
    public async Task Publish_AddsRowOnSaveChanges_AndPersistsEventTypeAndPayload()
    {
        var @event = new TestOutboxEvent(Guid.NewGuid(), "hello", DateTime.UtcNow);

        await PublishAsync(@event);

        var rows = await AllOutboxAsync();
        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal(@event.EventId, row.Id);
        Assert.Equal(OutboxMessageStatus.PENDING, row.Status);
        Assert.Null(row.ProcessedAt);
        Assert.Equal(0, row.RetryCount);
        Assert.Contains("TestOutboxEvent", row.EventType);
        Assert.Contains("hello", row.Payload);
    }

    [Fact]
    public async Task Publish_WithoutSaveChanges_PersistsNothing_ProvingAtomicity()
    {
        // 09 §9.3 — outbox row only commits if the caller's UoW saves; rolling
        // back the change tracker without SaveChanges leaves zero rows.
        using var scope = _serviceProvider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();

        await outbox.PublishAsync(new TestOutboxEvent(Guid.NewGuid(), "uncommitted", DateTime.UtcNow));
        // No SaveChangesAsync.

        var rows = await AllOutboxAsync();
        Assert.Empty(rows);
    }

    // ----------------------------- Dispatcher happy path ----------------

    [Fact]
    public async Task Dispatcher_ProcessesPendingEvent_PublishesToHandler_AndMarksProcessed()
    {
        var @event = new TestOutboxEvent(Guid.NewGuid(), "dispatch me", DateTime.UtcNow);
        await PublishAsync(@event);

        await RunDispatcherAsync();

        Assert.Single(TestOutboxEventHandler.Received);
        Assert.Equal(@event.EventId, TestOutboxEventHandler.Received[0].EventId);

        var rows = await AllOutboxAsync();
        Assert.Single(rows);
        Assert.Equal(OutboxMessageStatus.PROCESSED, rows[0].Status);
        Assert.NotNull(rows[0].ProcessedAt);
    }

    [Fact]
    public async Task Dispatcher_DispatchesByConcreteType_RoutingToCorrectHandler()
    {
        await PublishAsync(new TestOutboxEvent(Guid.NewGuid(), "first", DateTime.UtcNow));
        await PublishAsync(new SecondTestOutboxEvent(Guid.NewGuid(), 42, DateTime.UtcNow));

        await RunDispatcherAsync();

        Assert.Single(TestOutboxEventHandler.Received);
        Assert.Single(SecondTestOutboxEventHandler.Received);
        Assert.Equal(42, SecondTestOutboxEventHandler.Received[0].Number);
    }

    [Fact]
    public async Task Dispatcher_RunTwiceOnSameEvent_DoesNotProcessProcessedRowAgain()
    {
        await PublishAsync(new TestOutboxEvent(Guid.NewGuid(), "once", DateTime.UtcNow));

        await RunDispatcherAsync();
        await RunDispatcherAsync();

        // Second dispatcher run should observe Status=PROCESSED and skip.
        Assert.Single(TestOutboxEventHandler.Received);
    }

    // ----------------------------- Failure / retry / max retry ----------

    [Fact]
    public async Task Dispatcher_OnHandlerException_MarksFailed_IncrementsRetryCount_AndStoresError()
    {
        var @event = new AlwaysFailingTestEvent(Guid.NewGuid(), DateTime.UtcNow);
        await PublishAsync(@event);

        await RunDispatcherAsync();

        var row = (await AllOutboxAsync()).Single();
        Assert.Equal(OutboxMessageStatus.FAILED, row.Status);
        Assert.Equal(1, row.RetryCount);
        Assert.NotNull(row.ErrorMessage);
        Assert.Contains("intentional test failure", row.ErrorMessage);
    }

    [Fact]
    public async Task Dispatcher_PicksUpFailedRowsOnSubsequentIterations_AndKeepsRetrying()
    {
        await PublishAsync(new AlwaysFailingTestEvent(Guid.NewGuid(), DateTime.UtcNow));

        // 06 §3.18 — dispatcher pulls PENDING + FAILED together; consecutive
        // iterations keep retrying the same row until MaxRetryCount.
        await RunDispatcherAsync();
        await RunDispatcherAsync();

        var row = (await AllOutboxAsync()).Single();
        Assert.Equal(OutboxMessageStatus.FAILED, row.Status);
        Assert.Equal(2, row.RetryCount);
    }

    [Fact]
    public async Task Dispatcher_OnMaxRetryReached_RaisesAdminAlert_AndKeepsRowFailed()
    {
        await PublishAsync(new AlwaysFailingTestEvent(Guid.NewGuid(), DateTime.UtcNow));

        // MaxRetryCount = 3 in test options.
        await RunDispatcherAsync();
        await RunDispatcherAsync();
        await RunDispatcherAsync();

        var row = (await AllOutboxAsync()).Single();
        Assert.Equal(OutboxMessageStatus.FAILED, row.Status);
        Assert.Equal(3, row.RetryCount);

        var sink = _serviceProvider.GetRequiredService<SpyOutboxAdminAlertSink>();
        Assert.Single(sink.Raised);
        Assert.Equal(row.Id, sink.Raised[0].Id);
    }

    // ----------------------------- Self-rescheduling --------------------

    [Fact]
    public async Task Dispatcher_AlwaysReschedulesItself_EvenWhenNothingToDo()
    {
        await RunDispatcherAsync();

        var spy = _serviceProvider.GetRequiredService<SpyJobScheduler>();
        Assert.Single(spy.ScheduledDelays);
        Assert.Equal(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), spy.ScheduledDelays[0]);
    }

    [Fact]
    public async Task Dispatcher_ReschedulesItself_EvenWhenBatchHandlerThrows()
    {
        // Verifies the try/finally branch in 09 §13.4: chain must NEVER break.
        await PublishAsync(new AlwaysFailingTestEvent(Guid.NewGuid(), DateTime.UtcNow));

        await RunDispatcherAsync();

        var spy = _serviceProvider.GetRequiredService<SpyJobScheduler>();
        Assert.Single(spy.ScheduledDelays);
    }

    // ----------------------------- Distributed lock ---------------------

    [Fact]
    public async Task Dispatcher_WhenLockHeldByAnotherInstance_SkipsBatch_ButStillReschedules()
    {
        // Acquire the lock outside the dispatcher to simulate a parallel
        // instance owning it.
        var lockProvider = _serviceProvider.GetRequiredService<IDistributedLockProvider>();
        await using var heldLock = await lockProvider
            .CreateLock(OutboxDispatcher.LockName)
            .TryAcquireAsync(TimeSpan.Zero);

        Assert.NotNull(heldLock);

        await PublishAsync(new TestOutboxEvent(Guid.NewGuid(), "should be skipped", DateTime.UtcNow));

        await RunDispatcherAsync();

        // Batch was skipped → no handler invocation, row still PENDING.
        Assert.Empty(TestOutboxEventHandler.Received);
        var row = (await AllOutboxAsync()).Single();
        Assert.Equal(OutboxMessageStatus.PENDING, row.Status);

        // But the chain MUST still reschedule itself.
        var spy = _serviceProvider.GetRequiredService<SpyJobScheduler>();
        Assert.Single(spy.ScheduledDelays);
    }

    // ----------------------------- External idempotency -----------------

    [Fact]
    public async Task ExternalIdempotency_AcquireFresh_ReturnsAcquired_AndPersistsInProgress()
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IExternalIdempotencyService>();

        var result = await svc.AcquireAsync("SteamSidecar", "key-1", TimeSpan.FromMinutes(1));

        Assert.IsType<ExternalIdempotencyAcquisition.Acquired>(result);

        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await ctx.ExternalIdempotencyRecords.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalIdempotencyStatus.in_progress, row.Status);
        Assert.NotNull(row.LeaseExpiresAt);
        Assert.Null(row.CompletedAt);
    }

    [Fact]
    public async Task ExternalIdempotency_AcquireThenComplete_ThenSecondAcquireReplaysResult()
    {
        // First call acquires and completes with a payload.
        using (var scope1 = _serviceProvider.CreateScope())
        {
            var svc = scope1.ServiceProvider.GetRequiredService<IExternalIdempotencyService>();
            var first = await svc.AcquireAsync("SteamSidecar", "key-2", TimeSpan.FromMinutes(1));
            Assert.IsType<ExternalIdempotencyAcquisition.Acquired>(first);
            await svc.CompleteAsync("SteamSidecar", "key-2", "{\"offerId\":\"abc\"}");
        }

        // Second call sees completed record and replays the stored payload.
        using var scope2 = _serviceProvider.CreateScope();
        var svc2 = scope2.ServiceProvider.GetRequiredService<IExternalIdempotencyService>();
        var second = await svc2.AcquireAsync("SteamSidecar", "key-2", TimeSpan.FromMinutes(1));

        var replay = Assert.IsType<ExternalIdempotencyAcquisition.Replay>(second);
        Assert.Equal("{\"offerId\":\"abc\"}", replay.ResultPayload);
    }

    [Fact]
    public async Task ExternalIdempotency_AcquireWhileInProgressLeaseValid_Blocks()
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IExternalIdempotencyService>();

        var first = await svc.AcquireAsync("SteamSidecar", "key-3", TimeSpan.FromMinutes(5));
        Assert.IsType<ExternalIdempotencyAcquisition.Acquired>(first);

        var second = await svc.AcquireAsync("SteamSidecar", "key-3", TimeSpan.FromMinutes(5));
        Assert.IsType<ExternalIdempotencyAcquisition.Blocked>(second);
    }

    [Fact]
    public async Task ExternalIdempotency_StaleLease_IsReclaimedToFailed_ThenAcquired()
    {
        // Manually insert an expired in_progress row to simulate a crashed
        // previous caller (06 §3.21 stale recovery).
        using (var scope = _serviceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ctx.ExternalIdempotencyRecords.Add(new ExternalIdempotencyRecord
            {
                ServiceName = "BlockchainService",
                IdempotencyKey = "stale-1",
                Status = ExternalIdempotencyStatus.in_progress,
                LeaseExpiresAt = DateTime.UtcNow.AddSeconds(-30),
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            });
            await ctx.SaveChangesAsync();
        }

        using var acquireScope = _serviceProvider.CreateScope();
        var svc = acquireScope.ServiceProvider.GetRequiredService<IExternalIdempotencyService>();

        var result = await svc.AcquireAsync("BlockchainService", "stale-1", TimeSpan.FromMinutes(1));

        Assert.IsType<ExternalIdempotencyAcquisition.Acquired>(result);

        var ctx2 = acquireScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await ctx2.ExternalIdempotencyRecords.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalIdempotencyStatus.in_progress, row.Status);
        Assert.NotNull(row.LeaseExpiresAt);
        Assert.True(row.LeaseExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task ExternalIdempotency_AcquireFailedRecord_AtomicallyClaimsAndReturnsAcquired()
    {
        // Seed a failed row directly.
        using (var seedScope = _serviceProvider.CreateScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            ctx.ExternalIdempotencyRecords.Add(new ExternalIdempotencyRecord
            {
                ServiceName = "SteamSidecar",
                IdempotencyKey = "key-failed",
                Status = ExternalIdempotencyStatus.failed,
                LeaseExpiresAt = null,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            });
            await ctx.SaveChangesAsync();
        }

        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IExternalIdempotencyService>();

        var result = await svc.AcquireAsync("SteamSidecar", "key-failed", TimeSpan.FromMinutes(1));
        Assert.IsType<ExternalIdempotencyAcquisition.Acquired>(result);

        var ctx2 = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await ctx2.ExternalIdempotencyRecords.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalIdempotencyStatus.in_progress, row.Status);
        Assert.NotNull(row.LeaseExpiresAt);
    }

    [Fact]
    public async Task ExternalIdempotency_FailAsync_TransitionsInProgressToFailed_AndClearsLease()
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IExternalIdempotencyService>();

        await svc.AcquireAsync("SteamSidecar", "key-fail", TimeSpan.FromMinutes(1));
        await svc.FailAsync("SteamSidecar", "key-fail");

        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await ctx.ExternalIdempotencyRecords.AsNoTracking().SingleAsync();
        Assert.Equal(ExternalIdempotencyStatus.failed, row.Status);
        Assert.Null(row.LeaseExpiresAt);
        Assert.Null(row.CompletedAt);
    }

    [Fact]
    public async Task ExternalIdempotency_CompleteAsync_WithoutAcquireFirst_Throws()
    {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IExternalIdempotencyService>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CompleteAsync("SteamSidecar", "missing-key", null));
    }

    // ----------------------------- Round-trip serialization -------------

    [Fact]
    public async Task Dispatcher_PreservesEventPayloadAcrossSerializationRoundTrip()
    {
        var occurredAt = new DateTime(2026, 4, 7, 12, 0, 0, DateTimeKind.Utc);
        var @event = new TestOutboxEvent(Guid.NewGuid(), "round-trip body", occurredAt);

        await PublishAsync(@event);

        // Verify serialized payload deserializes back to the same record.
        var row = (await AllOutboxAsync()).Single();
        var resolved = OutboxEventTypeName.Resolve(row.EventType);
        Assert.NotNull(resolved);
        var deserialized = (TestOutboxEvent?)JsonSerializer.Deserialize(
            row.Payload, resolved, OutboxService.PayloadSerializerOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(@event.EventId, deserialized!.EventId);
        Assert.Equal(@event.Body, deserialized.Body);
        Assert.Equal(@event.OccurredAt, deserialized.OccurredAt);

        // And the dispatcher actually publishes the deserialized form to the
        // handler — proves the full read path.
        await RunDispatcherAsync();
        Assert.Single(TestOutboxEventHandler.Received);
        Assert.Equal(@event.Body, TestOutboxEventHandler.Received[0].Body);
        Assert.Equal(occurredAt, TestOutboxEventHandler.Received[0].OccurredAt);
    }

    // ----------------------------- ProcessedEventStore ------------------

    [Fact]
    public async Task ProcessedEventStore_ExistsAfterMark_AndRequiresSaveChanges()
    {
        var eventId = Guid.NewGuid();

        using (var scope = _serviceProvider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IProcessedEventStore>();
            var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Assert.False(await store.ExistsAsync(eventId, "TestConsumer"));

            await store.MarkAsProcessedAsync(eventId, "TestConsumer");
            // Mark adds to change tracker but doesn't save — caller controls
            // the transaction (09 §9.3).
            Assert.False(await store.ExistsAsync(eventId, "TestConsumer"));

            await ctx.SaveChangesAsync();
            Assert.True(await store.ExistsAsync(eventId, "TestConsumer"));
        }

        using (var scope2 = _serviceProvider.CreateScope())
        {
            var store2 = scope2.ServiceProvider.GetRequiredService<IProcessedEventStore>();
            Assert.True(await store2.ExistsAsync(eventId, "TestConsumer"));
            // Different consumer name — independent record.
            Assert.False(await store2.ExistsAsync(eventId, "OtherConsumer"));
        }
    }
}
