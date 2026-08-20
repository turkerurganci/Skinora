using Microsoft.Extensions.Logging.Abstractions;
using Skinora.Shared.Enums;
using Skinora.Shared.Events;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.PaymentMonitoring;
using Skinora.Transactions.Tests.Integration.PaymentAddresses;

namespace Skinora.Transactions.Tests.Unit.PaymentMonitoring;

/// <summary>
/// T139 — <see cref="PaymentMonitorStartDispatcher"/>, the outbox fast path
/// that arms the sidecar when the buyer's payment window opens. The three
/// branches mirror the T75 post-cancel dispatcher: acknowledged, terminal
/// rejection, retryable failure.
/// </summary>
public class PaymentMonitorStartDispatcherTests
{
    private const string DepositAddress = "TPaymentMonitorDepositAddrFakeXXXX";

    [Fact]
    public async Task Success_Arms_The_Sidecar_With_The_Mapped_Payload()
    {
        var sidecar = new StubBlockchainSidecarClient();
        var notification = BuildEvent();

        await BuildSut(sidecar).Handle(notification, CancellationToken.None);

        var call = Assert.Single(sidecar.MonitorStartCalls);
        Assert.Equal(notification.Address, call.Address);
        Assert.Equal(notification.PaymentAddressId, call.PaymentAddressId);
        Assert.Equal(notification.TransactionId, call.TransactionId);
        Assert.Equal(notification.ExpectedContractAddress, call.ExpectedContract);
        // The sidecar's handler validates the symbol against its own
        // USDT/USDC allowlist, so the enum must arrive as its name.
        Assert.Equal("USDT", call.ExpectedSymbol);
    }

    [Fact]
    public async Task InvalidRequest_Is_Terminal_And_Does_Not_Throw()
    {
        // 400 means the payload itself is wrong — re-delivering it would fail
        // identically, so the outbox chain must not be kept alive by an
        // exception. The per-minute reconciler keeps retrying, which is what
        // turns a persistent 400 into a repeating warning instead of silence.
        var sidecar = new StubBlockchainSidecarClient();
        sidecar.MonitorStartResponses.Enqueue(BlockchainSidecarStatus.InvalidRequest);

        await BuildSut(sidecar).Handle(BuildEvent(), CancellationToken.None);

        Assert.Single(sidecar.MonitorStartCalls);
    }

    [Theory]
    [InlineData(BlockchainSidecarStatus.Unavailable)]
    [InlineData(BlockchainSidecarStatus.NotConfigured)]
    public async Task Transport_Failure_Throws_So_The_Outbox_Retries(
        BlockchainSidecarStatus status)
    {
        var sidecar = new StubBlockchainSidecarClient();
        sidecar.MonitorStartResponses.Enqueue(status);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildSut(sidecar).Handle(BuildEvent(), CancellationToken.None));
    }

    private static PaymentMonitorStartDispatcher BuildSut(IBlockchainSidecarClient sidecar)
        => new(sidecar, NullLogger<PaymentMonitorStartDispatcher>.Instance);

    private static PaymentMonitorStartRequestedEvent BuildEvent()
        => new(
            EventId: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            PaymentAddressId: Guid.NewGuid(),
            Address: DepositAddress,
            ExpectedToken: StablecoinType.USDT,
            ExpectedContractAddress: "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t",
            OccurredAt: new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc));
}
