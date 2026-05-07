using Skinora.Realtime.Application;
using Skinora.Realtime.Application.Contracts;

namespace Skinora.Realtime.Tests.Unit;

/// <summary>
/// Test double that records every publish call so consumer unit tests can
/// assert payload content without spinning up a real SignalR hub. Captures
/// payloads as <see cref="object"/> records for the recorder; tests cast back
/// to the concrete payload type they expect.
/// </summary>
public sealed class RecordingRealtimePublisher : ITransactionRealtimePublisher
{
    public List<(string Method, object Payload)> Calls { get; } = [];

    public Task PublishStatusChangedAsync(
        TransactionRealtimePayloads.TransactionStatusChanged payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("StatusChanged", payload));
        return Task.CompletedTask;
    }

    public Task PublishCountdownSyncAsync(
        TransactionRealtimePayloads.CountdownSync payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("CountdownSync", payload));
        return Task.CompletedTask;
    }

    public Task PublishPaymentDetectedAsync(
        TransactionRealtimePayloads.PaymentDetected payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("PaymentDetected", payload));
        return Task.CompletedTask;
    }

    public Task PublishPaymentConfirmedAsync(
        TransactionRealtimePayloads.PaymentConfirmed payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("PaymentConfirmed", payload));
        return Task.CompletedTask;
    }

    public Task PublishDisputeUpdateAsync(
        TransactionRealtimePayloads.DisputeUpdate payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("DisputeUpdate", payload));
        return Task.CompletedTask;
    }

    public Task PublishFlagResolvedAsync(
        TransactionRealtimePayloads.FlagResolved payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("FlagResolved", payload));
        return Task.CompletedTask;
    }

    public Task PublishEmergencyHoldAppliedAsync(
        TransactionRealtimePayloads.EmergencyHoldApplied payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("EmergencyHoldApplied", payload));
        return Task.CompletedTask;
    }

    public Task PublishEmergencyHoldReleasedAsync(
        TransactionRealtimePayloads.EmergencyHoldReleased payload,
        CancellationToken cancellationToken)
    {
        Calls.Add(("EmergencyHoldReleased", payload));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Minimal in-memory <see cref="Skinora.Shared.Outbox.IProcessedEventStore"/>
/// for consumer unit tests — mirrors the implementation used in
/// <c>TransactionCancelledNotificationConsumerTests</c>.
/// </summary>
public sealed class InMemoryProcessedEventStore : Skinora.Shared.Outbox.IProcessedEventStore
{
    private readonly HashSet<(Guid eventId, string consumer)> _entries = [];

    public Task<bool> ExistsAsync(
        Guid eventId, string consumerName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.Contains((eventId, consumerName)));

    public Task MarkAsProcessedAsync(
        Guid eventId, string consumerName,
        CancellationToken cancellationToken = default)
    {
        _entries.Add((eventId, consumerName));
        return Task.CompletedTask;
    }
}
