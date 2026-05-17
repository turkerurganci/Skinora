using Microsoft.Extensions.Logging.Abstractions;
using Skinora.API.Services.Reconciliation;
using Skinora.Transactions.Application.Reconciliation;

namespace Skinora.API.Tests.Unit.Reconciliation;

/// <summary>
/// Unit coverage for the Hangfire wrapper <see cref="ReconciliationJob"/>
/// (T76). The job is a thin DI ↔ scheduler shim; these tests verify it
/// delegates to <see cref="IReconciliationService"/> and surfaces service
/// failures so Hangfire records the run as failed (and triggers the
/// default retry policy).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ReconciliationJobTests
{
    [Fact]
    public async Task ExecuteAsync_DelegatesToService_AndLogsOutcome()
    {
        var service = new StubReconciliationService
        {
            NextOutcome = new ReconciliationOutcome(
                DepositAddressesChecked: 3,
                HotWalletChecked: true,
                ColdWalletChecked: false,
                MismatchCount: 0,
                BlockNumber: 12_345),
        };
        var job = new ReconciliationJob(service, NullLogger<ReconciliationJob>.Instance);

        await job.ExecuteAsync();

        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceThrows_PropagatesForHangfireRetry()
    {
        var service = new StubReconciliationService { Throw = new InvalidOperationException("boom") };
        var job = new ReconciliationJob(service, NullLogger<ReconciliationJob>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync());
        Assert.Equal("boom", ex.Message);
        Assert.Equal(1, service.Calls);
    }

    private sealed class StubReconciliationService : IReconciliationService
    {
        public int Calls { get; private set; }
        public ReconciliationOutcome NextOutcome { get; init; } =
            new(0, false, false, 0, null);
        public Exception? Throw { get; init; }

        public Task<ReconciliationOutcome> RunAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw is not null) throw Throw;
            return Task.FromResult(NextOutcome);
        }
    }
}
