using Hangfire;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Transfers;

namespace Skinora.Transactions.Tests.Unit.Transfers;

/// <summary>
/// The transfer broadcast budget and the dispatcher's one-tick-at-a-time rule
/// (#323 validation, 2026-09-17). A deposit-sourced broadcast now waits on the
/// sidecar for every resource step to land in a block (08 §3.3): the sidecar
/// stops before a transfer broadcast 150 s into the call and waits up to ~75 s
/// for the transfer's block afterwards. A budget below that abandons calls the
/// sidecar is still running, and a second dispatcher tick overlapping the first
/// runs the same deposit's flow twice.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BlockchainSidecarTimeoutTests
{
    [Fact]
    public void TransferTimeout_Is300Seconds_NotDerivedFromTheChainReadTimeout()
    {
        // The pre-fix budget was TimeoutSeconds × 3 = 30 s for every call.
        var options = new BlockchainSidecarOptions { TimeoutSeconds = 10 };

        Assert.Equal(TimeSpan.FromSeconds(300), options.ResolveTransferTimeout());
        Assert.Equal(TimeSpan.FromSeconds(30), options.ResolveChainReadTimeout());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void TransferTimeout_FallsBackToTheDefault_WhenNotPositive(int configured)
    {
        var options = new BlockchainSidecarOptions { TransferTimeoutSeconds = configured };

        Assert.Equal(
            TimeSpan.FromSeconds(BlockchainSidecarOptions.DefaultTransferTimeoutSeconds),
            options.ResolveTransferTimeout());
    }

    [Fact]
    public void TransferTimeout_HonoursAnOperatorValue()
    {
        var options = new BlockchainSidecarOptions { TransferTimeoutSeconds = 420 };

        Assert.Equal(TimeSpan.FromSeconds(420), options.ResolveTransferTimeout());
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(7, 21)]
    public void ChainReadTimeout_IsThreeTimesTimeoutSeconds_Or30WhenUnset(int timeoutSeconds, int expected)
    {
        var options = new BlockchainSidecarOptions { TimeoutSeconds = timeoutSeconds };

        Assert.Equal(TimeSpan.FromSeconds(expected), options.ResolveChainReadTimeout());
    }

    [Fact]
    public void DispatchJob_RunsOneTickAtATime()
    {
        var execute = typeof(OutgoingTransferDispatchJob).GetMethod(nameof(OutgoingTransferDispatchJob.Execute))!;

        Assert.Single(execute.GetCustomAttributes(typeof(DisableConcurrentExecutionAttribute), inherit: false));
        // A tick that cannot take the lock must give up before the next one fires.
        Assert.InRange(OutgoingTransferDispatchJob.ConcurrencyLockTimeoutSeconds, 1, 59);
    }
}
