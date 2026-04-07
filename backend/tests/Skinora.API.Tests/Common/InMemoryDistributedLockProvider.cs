using System.Collections.Concurrent;
using Medallion.Threading;

namespace Skinora.API.Tests.Common;

/// <summary>
/// Test stub for <see cref="IDistributedLockProvider"/> backed by an
/// in-process <see cref="SemaphoreSlim"/> per lock name.
/// </summary>
/// <remarks>
/// <para>
/// The production OutboxModule registers Medallion's
/// <c>SqlDistributedSynchronizationProvider</c>, which opens a real SQL
/// Server connection at acquire time. The test factory replaces it with this
/// stub so OutboxDispatcher's tekillik garantisi (09 §13.4) can still be
/// exercised end-to-end without a database.
/// </para>
/// <para>
/// Single-process semantics: identical lock names share a semaphore, so a
/// second concurrent acquire from the same process returns null when called
/// with <see cref="TimeSpan.Zero"/> — exactly the contract OutboxDispatcher
/// relies on.
/// </para>
/// </remarks>
public class InMemoryDistributedLockProvider : IDistributedLockProvider
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Semaphores = new();

    public IDistributedLock CreateLock(string name)
        => new InMemoryDistributedLock(name, Semaphores.GetOrAdd(name, _ => new SemaphoreSlim(1, 1)));

    private sealed class InMemoryDistributedLock : IDistributedLock
    {
        private readonly SemaphoreSlim _semaphore;

        public InMemoryDistributedLock(string name, SemaphoreSlim semaphore)
        {
            Name = name;
            _semaphore = semaphore;
        }

        public string Name { get; }

        public IDistributedSynchronizationHandle? TryAcquire(
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default)
        {
            var acquired = _semaphore.Wait(timeout, cancellationToken);
            return acquired ? new InMemoryHandle(_semaphore) : null;
        }

        public async ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(
            TimeSpan timeout = default,
            CancellationToken cancellationToken = default)
        {
            var acquired = await _semaphore.WaitAsync(timeout, cancellationToken);
            return acquired ? new InMemoryHandle(_semaphore) : null;
        }

        public IDistributedSynchronizationHandle Acquire(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            _semaphore.Wait(timeout ?? Timeout.InfiniteTimeSpan, cancellationToken);
            return new InMemoryHandle(_semaphore);
        }

        public async ValueTask<IDistributedSynchronizationHandle> AcquireAsync(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(timeout ?? Timeout.InfiniteTimeSpan, cancellationToken);
            return new InMemoryHandle(_semaphore);
        }
    }

    private sealed class InMemoryHandle : IDistributedSynchronizationHandle
    {
        private SemaphoreSlim? _semaphore;

        public InMemoryHandle(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public CancellationToken HandleLostToken => CancellationToken.None;

        public void Dispose()
        {
            var s = Interlocked.Exchange(ref _semaphore, null);
            s?.Release();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
