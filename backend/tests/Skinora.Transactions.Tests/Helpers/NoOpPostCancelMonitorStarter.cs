using Skinora.Transactions.Application.PostCancel;

namespace Skinora.Transactions.Tests.Helpers;

/// <summary>
/// Test-only no-op <see cref="IPostCancelMonitorStarter"/>. Cancel-flow tests
/// that do not assert on post-cancel side effects use this stub so the
/// constructor signature stays satisfied without pulling in real EF Core /
/// outbox plumbing. Dedicated T75 fixtures use the production
/// <see cref="PostCancelMonitorStarter"/> against the in-memory DB.
/// </summary>
internal sealed class NoOpPostCancelMonitorStarter : IPostCancelMonitorStarter
{
    public int Calls { get; private set; }
    public List<(Guid TransactionId, DateTime CancelledAt)> Captured { get; } = new();

    public Task RequestStartAsync(
        Guid transactionId,
        DateTime cancelledAt,
        CancellationToken cancellationToken)
    {
        Calls++;
        Captured.Add((transactionId, cancelledAt));
        return Task.CompletedTask;
    }
}
