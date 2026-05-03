using System.Globalization;
using System.Text.Json;
using Skinora.Platform.Application.Audit;
using Skinora.Shared.Domain.Seed;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Shared.Interfaces;

namespace Skinora.Transactions.Application.GasFee;

/// <inheritdoc cref="IRefundBlockedAlertService"/>
public sealed class RefundBlockedAlertService : IRefundBlockedAlertService
{
    private const string EntityType = "Transaction";

    private readonly IAuditLogger _audit;
    private readonly IOutboxService _outbox;
    private readonly TimeProvider _clock;

    public RefundBlockedAlertService(
        IAuditLogger audit,
        IOutboxService outbox,
        TimeProvider clock)
    {
        _audit = audit;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task RaiseAsync(Guid transactionId, RefundDecision decision, CancellationToken cancellationToken)
    {
        if (decision.Outcome != RefundOutcome.Block)
            throw new InvalidOperationException(
                $"RefundBlockedAlertService.RaiseAsync expects a blocked decision; got {decision.Outcome}.");
        if (decision.Reason is null)
            throw new InvalidOperationException(
                "Blocked RefundDecision must carry a non-null Reason.");

        var occurredAt = _clock.GetUtcNow().UtcDateTime;

        await _audit.LogAsync(
            new AuditLogEntry(
                UserId: null,
                ActorId: SeedConstants.SystemUserId,
                ActorType: ActorType.SYSTEM,
                Action: AuditAction.REFUND_BLOCKED,
                EntityType: EntityType,
                EntityId: transactionId.ToString("D"),
                OldValue: null,
                NewValue: SerializeDetails(decision),
                IpAddress: null),
            cancellationToken);

        await _outbox.PublishAsync(
            new RefundBlockedAdminAlertEvent(
                EventId: Guid.NewGuid(),
                TransactionId: transactionId,
                Reason: decision.Reason.Value,
                TotalPaid: decision.TotalPaid,
                GasFee: decision.GasFee,
                NetRefund: decision.NetRefund,
                MinimumThreshold: decision.Threshold,
                OccurredAt: occurredAt),
            cancellationToken);
    }

    private static string SerializeDetails(RefundDecision decision) =>
        JsonSerializer.Serialize(new
        {
            reason = decision.Reason!.Value.ToString(),
            totalPaid = decision.TotalPaid.ToString(CultureInfo.InvariantCulture),
            gasFee = decision.GasFee.ToString(CultureInfo.InvariantCulture),
            netRefund = decision.NetRefund.ToString(CultureInfo.InvariantCulture),
            minimumThreshold = decision.Threshold.ToString(CultureInfo.InvariantCulture),
        });
}
