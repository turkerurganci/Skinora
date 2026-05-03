using Microsoft.Extensions.Time.Testing;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Domain;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;
using Skinora.Transactions.Application.GasFee;

namespace Skinora.Transactions.Tests.Unit.GasFee;

/// <summary>
/// Unit coverage for <see cref="RefundBlockedAlertService"/> — verifies the
/// audit row + outbox event fan-out without touching a real DbContext.
/// </summary>
public class RefundBlockedAlertServiceTests
{
    private sealed class CapturingAuditLogger : IAuditLogger
    {
        public List<AuditLogEntry> Entries { get; } = new();

        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingOutboxService : IOutboxService
    {
        public List<IDomainEvent> Events { get; } = new();

        public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private static (RefundBlockedAlertService svc,
                    CapturingAuditLogger audit,
                    CapturingOutboxService outbox,
                    FakeTimeProvider clock) NewService()
    {
        var audit = new CapturingAuditLogger();
        var outbox = new CapturingOutboxService();
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(new DateTimeOffset(2026, 5, 3, 12, 0, 0, TimeSpan.Zero));
        return (new RefundBlockedAlertService(audit, outbox, clock), audit, outbox, clock);
    }

    private static RefundDecision BlockedSubThreshold(decimal totalPaid = 2.5m, decimal gasFee = 1m, decimal threshold = 2m) =>
        new(RefundOutcome.Block, NetRefund: totalPaid - gasFee, Threshold: threshold,
            GasFee: gasFee, TotalPaid: totalPaid, Reason: RefundBlockedReason.BelowMinimumThreshold);

    private static RefundDecision BlockedNegative(decimal totalPaid = 1m, decimal gasFee = 5m, decimal threshold = 10m) =>
        new(RefundOutcome.Block, NetRefund: totalPaid - gasFee, Threshold: threshold,
            GasFee: gasFee, TotalPaid: totalPaid, Reason: RefundBlockedReason.NegativeAmount);

    [Fact]
    public async Task RaiseAsync_StagesAuditRowWith_REFUND_BLOCKED_Action()
    {
        var (svc, audit, _, _) = NewService();
        var txId = Guid.NewGuid();

        await svc.RaiseAsync(txId, BlockedSubThreshold(), default);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuditAction.REFUND_BLOCKED, entry.Action);
        Assert.Equal(ActorType.SYSTEM, entry.ActorType);
        Assert.Equal(SeedConstants.SystemUserId, entry.ActorId);
        Assert.Null(entry.UserId);
        Assert.Equal("Transaction", entry.EntityType);
        Assert.Equal(txId.ToString("D"), entry.EntityId);
        Assert.Null(entry.OldValue);
        Assert.Null(entry.IpAddress);
    }

    [Fact]
    public async Task RaiseAsync_AuditNewValue_ContainsDecisionDetailsAsJson()
    {
        var (svc, audit, _, _) = NewService();
        var decision = BlockedSubThreshold(totalPaid: 2.5m, gasFee: 1m, threshold: 2m);

        await svc.RaiseAsync(Guid.NewGuid(), decision, default);

        var entry = Assert.Single(audit.Entries);
        Assert.NotNull(entry.NewValue);
        // Loose containment assertions — the exact JSON shape is internal to
        // the service but every field on the decision must be discoverable
        // by an admin auditing the row.
        Assert.Contains("BelowMinimumThreshold", entry.NewValue);
        Assert.Contains("2.5", entry.NewValue);   // totalPaid
        Assert.Contains("1.5", entry.NewValue);   // netRefund (2.5 - 1)
        Assert.Contains("2", entry.NewValue);     // threshold
    }

    [Fact]
    public async Task RaiseAsync_PublishesRefundBlockedAdminAlertEventToOutbox()
    {
        var (svc, _, outbox, clock) = NewService();
        var txId = Guid.NewGuid();
        var decision = BlockedSubThreshold(totalPaid: 2.5m, gasFee: 1m, threshold: 2m);

        await svc.RaiseAsync(txId, decision, default);

        var domainEvent = Assert.Single(outbox.Events);
        var alert = Assert.IsType<RefundBlockedAdminAlertEvent>(domainEvent);
        Assert.NotEqual(Guid.Empty, alert.EventId);
        Assert.Equal(txId, alert.TransactionId);
        Assert.Equal(RefundBlockedReason.BelowMinimumThreshold, alert.Reason);
        Assert.Equal(2.5m, alert.TotalPaid);
        Assert.Equal(1m, alert.GasFee);
        Assert.Equal(1.5m, alert.NetRefund);
        Assert.Equal(2m, alert.MinimumThreshold);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, alert.OccurredAt);
    }

    [Fact]
    public async Task RaiseAsync_NegativeReason_PreservedOnEvent()
    {
        var (svc, _, outbox, _) = NewService();
        var decision = BlockedNegative();

        await svc.RaiseAsync(Guid.NewGuid(), decision, default);

        var alert = Assert.IsType<RefundBlockedAdminAlertEvent>(outbox.Events[0]);
        Assert.Equal(RefundBlockedReason.NegativeAmount, alert.Reason);
    }

    [Fact]
    public async Task RaiseAsync_DoesNotPublishWhenDecisionIsRefund()
    {
        var (svc, _, _, _) = NewService();
        var refundDecision = new RefundDecision(
            Outcome: RefundOutcome.Refund,
            NetRefund: 100m,
            Threshold: 2m,
            GasFee: 1m,
            TotalPaid: 101m,
            Reason: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RaiseAsync(Guid.NewGuid(), refundDecision, default));
    }

    [Fact]
    public async Task RaiseAsync_BlockedDecisionWithoutReason_Throws()
    {
        // The service contract guarantees Reason is non-null on Block — guard
        // against a future caller violating it.
        var (svc, _, _, _) = NewService();
        var malformed = new RefundDecision(
            Outcome: RefundOutcome.Block,
            NetRefund: 0m,
            Threshold: 0m,
            GasFee: 0m,
            TotalPaid: 0m,
            Reason: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RaiseAsync(Guid.NewGuid(), malformed, default));
    }

    [Fact]
    public async Task RaiseAsync_GeneratesUniqueEventIdPerCall()
    {
        var (svc, _, outbox, _) = NewService();

        await svc.RaiseAsync(Guid.NewGuid(), BlockedSubThreshold(), default);
        await svc.RaiseAsync(Guid.NewGuid(), BlockedSubThreshold(), default);

        var first = Assert.IsType<RefundBlockedAdminAlertEvent>(outbox.Events[0]);
        var second = Assert.IsType<RefundBlockedAdminAlertEvent>(outbox.Events[1]);
        Assert.NotEqual(first.EventId, second.EventId);
    }
}
