using System.Linq.Expressions;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.Notifications.Tests.TestSupport;

/// <summary>
/// Test double for <see cref="IBackgroundJobScheduler"/> — captures the
/// expressions passed to <see cref="Enqueue{T}"/> so a test can assert
/// "the dispatcher enqueued exactly N delivery jobs" without a real
/// Hangfire worker.
/// </summary>
public sealed class FakeBackgroundJobScheduler : IBackgroundJobScheduler
{
    public List<LambdaExpression> EnqueuedCalls { get; } = new();
    public List<LambdaExpression> ScheduledCalls { get; } = new();

    public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
    {
        ScheduledCalls.Add(methodCall);
        return Guid.NewGuid().ToString("N");
    }

    public string Enqueue<T>(Expression<Action<T>> methodCall)
    {
        EnqueuedCalls.Add(methodCall);
        return Guid.NewGuid().ToString("N");
    }

    public bool Delete(string jobId) => true;

    public void AddOrUpdateRecurring<T>(
        string jobId, Expression<Action<T>> methodCall, string cronExpression)
    {
    }
}
