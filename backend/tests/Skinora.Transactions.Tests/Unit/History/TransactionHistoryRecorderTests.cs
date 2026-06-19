using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;
using Skinora.Transactions.Application.History;
using Skinora.Transactions.Domain.Entities;
using Skinora.Transactions.Domain.StateMachine;
using Xunit;

namespace Skinora.Transactions.Tests.Unit.History;

/// <summary>
/// WP15 — unit coverage for <see cref="TransactionHistoryRecorder"/>. Confirms
/// the audit-trail row carries the captured PreviousStatus, the post-Fire
/// NewStatus, the trigger label, actor metadata, and that it is staged on the
/// caller's context (06 §3.6).
/// </summary>
[Trait("Category", "Unit")]
public class TransactionHistoryRecorderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public TransactionHistoryRecorderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public void Record_Transition_Captures_Prev_New_Trigger_And_Actor()
    {
        var tx = new Transaction { Id = Guid.NewGuid(), Status = TransactionStatus.COMPLETED };
        var occurredAt = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

        var row = TransactionHistoryRecorder.Record(
            _db, tx, TransactionStatus.ITEM_DELIVERED, TransactionTrigger.Complete,
            ActorType.SYSTEM, SeedConstants.SystemUserId, occurredAt);

        Assert.Equal(tx.Id, row.TransactionId);
        Assert.Equal(TransactionStatus.ITEM_DELIVERED, row.PreviousStatus);
        Assert.Equal(TransactionStatus.COMPLETED, row.NewStatus);
        Assert.Equal("Complete", row.Trigger);
        Assert.Equal(ActorType.SYSTEM, row.ActorType);
        Assert.Equal(SeedConstants.SystemUserId, row.ActorId);
        Assert.Equal(occurredAt, row.CreatedAt);
        // Staged on the caller's context — flushed by the caller's SaveChanges.
        Assert.Contains(row, _db.Set<TransactionHistory>().Local);
    }

    [Fact]
    public void Record_Genesis_Has_Null_PreviousStatus_And_Create_Trigger()
    {
        var sellerId = Guid.NewGuid();
        var tx = new Transaction { Id = Guid.NewGuid(), Status = TransactionStatus.FLAGGED, SellerId = sellerId };
        var occurredAt = new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc);

        var row = TransactionHistoryRecorder.Record(
            _db, tx, previousStatus: null, TransactionHistoryRecorder.GenesisTrigger,
            ActorType.USER, sellerId, occurredAt);

        Assert.Null(row.PreviousStatus);
        Assert.Equal(TransactionStatus.FLAGGED, row.NewStatus);
        Assert.Equal("Create", row.Trigger);
        Assert.Equal(ActorType.USER, row.ActorType);
        Assert.Equal(sellerId, row.ActorId);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
