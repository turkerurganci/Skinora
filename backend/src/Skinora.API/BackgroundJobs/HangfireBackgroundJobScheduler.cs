using System;
using System.Linq.Expressions;
using Hangfire;
using Skinora.Shared.BackgroundJobs;

namespace Skinora.API.BackgroundJobs;

/// <summary>
/// Hangfire-backed implementation of <see cref="IBackgroundJobScheduler"/>.
/// </summary>
/// <remarks>
/// Wraps the Hangfire <see cref="BackgroundJob"/> static API and the
/// <see cref="IBackgroundJobClient"/> service so that consumers in other modules
/// do not need a direct dependency on <c>Hangfire.Core</c>. Job scheduling
/// happens against the configured SQL Server storage and is durable across
/// application restarts (05 §2.5, 09 §13.1).
/// </remarks>
public sealed class HangfireBackgroundJobScheduler : IBackgroundJobScheduler
{
    private readonly IBackgroundJobClient _client;

    public HangfireBackgroundJobScheduler(IBackgroundJobClient client)
    {
        _client = client;
    }

    public string Schedule<T>(Expression<Action<T>> methodCall, TimeSpan delay)
        => _client.Schedule(methodCall, delay);

    public string Enqueue<T>(Expression<Action<T>> methodCall)
        => _client.Enqueue(methodCall);

    public bool Delete(string jobId)
        => _client.Delete(jobId);

    public void AddOrUpdateRecurring<T>(
        string jobId, Expression<Action<T>> methodCall, string cronExpression)
        => RecurringJob.AddOrUpdate(jobId, methodCall, cronExpression);
}
