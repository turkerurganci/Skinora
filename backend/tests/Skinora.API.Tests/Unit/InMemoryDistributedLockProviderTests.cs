using Skinora.API.Outbox;
using Skinora.API.Tests.Common;

namespace Skinora.API.Tests.Unit;

/// <summary>
/// Pins the isolation boundary of the test lock stub. The stub stands in for a
/// SQL Server lock, and every test has its own database, so two provider
/// instances (two tests) must never contend — while one instance must still
/// refuse a second holder (the OutboxDispatcher single-instance contract).
/// A process-wide lock table made OutboxTests fail whenever another test
/// class's dispatcher held "outbox-dispatcher" at the same moment.
/// </summary>
[Trait("Category", "Unit")]
public sealed class InMemoryDistributedLockProviderTests
{
    [Fact]
    public async Task Same_Provider_Refuses_A_Second_Holder_Of_The_Same_Lock()
    {
        var provider = new InMemoryDistributedLockProvider();

        await using var first = await provider.CreateLock(OutboxDispatcher.LockName).TryAcquireAsync(TimeSpan.Zero);
        await using var second = await provider.CreateLock(OutboxDispatcher.LockName).TryAcquireAsync(TimeSpan.Zero);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task Same_Provider_Hands_The_Lock_Over_Once_It_Is_Released()
    {
        var provider = new InMemoryDistributedLockProvider();

        var first = await provider.CreateLock(OutboxDispatcher.LockName).TryAcquireAsync(TimeSpan.Zero);
        Assert.NotNull(first);
        await first!.DisposeAsync();

        await using var second = await provider.CreateLock(OutboxDispatcher.LockName).TryAcquireAsync(TimeSpan.Zero);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task Separate_Providers_Do_Not_Share_Locks()
    {
        var otherTest = new InMemoryDistributedLockProvider();
        var thisTest = new InMemoryDistributedLockProvider();

        await using var heldElsewhere = await otherTest.CreateLock(OutboxDispatcher.LockName).TryAcquireAsync(TimeSpan.Zero);
        await using var mine = await thisTest.CreateLock(OutboxDispatcher.LockName).TryAcquireAsync(TimeSpan.Zero);

        Assert.NotNull(heldElsewhere);
        Assert.NotNull(mine);
    }
}
