using Moq;
using Skinora.API.Monitoring;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Networks;

namespace Skinora.API.Tests.Unit.Monitoring;

/// <summary>
/// T151 (kod düzeyi kapı) — Ethereum reaches <c>supportedNetworks</c> only
/// while the LAST probe found its service healthy, which includes
/// <c>acceptsPayments: true</c> (<see cref="SidecarHealthClient"/>). Not before
/// the first probe, not after a failed one, not while waiting for the outage
/// threshold. TRON keeps the T148 rule (closed only in a detected outage).
/// </summary>
[Trait("Category", "Unit")]
public sealed class PaymentCapabilityGateTests
{
    private const int Threshold = 3;

    private static (PlatformHealthMonitorState State, BlockchainNetworkAvailability Availability) Build()
    {
        var state = new PlatformHealthMonitorState();
        var services = new Mock<IBlockchainServiceRegistry>();
        services.Setup(s => s.IsConfigured(It.IsAny<BlockchainNetwork>())).Returns(true);
        return (state, new BlockchainNetworkAvailability(services.Object, new ProbeBlockchainNetworkHealth(state)));
    }

    [Fact]
    public void Ethereum_Is_Closed_Until_A_Probe_Has_Seen_It_Healthy()
    {
        var (state, availability) = Build();

        Assert.False(availability.IsOpenForNewTransactions(BlockchainNetwork.ETHEREUM));
        Assert.True(availability.IsOpenForNewTransactions(BlockchainNetwork.TRON));

        state.Record(PlatformComponents.BlockchainEthereum, healthy: true, Threshold);
        Assert.Equal(
            new[] { BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM },
            availability.OpenForNewTransactions());
    }

    [Fact]
    public void One_Probe_Without_The_Flag_Closes_Ethereum_At_Once_But_Is_Not_Yet_An_Outage()
    {
        var (state, availability) = Build();
        state.Record(PlatformComponents.BlockchainEthereum, healthy: true, Threshold);

        // acceptsPayments missing / false → the client reports unhealthy.
        state.Record(PlatformComponents.BlockchainEthereum, healthy: false, Threshold);

        Assert.False(availability.IsOpenForNewTransactions(BlockchainNetwork.ETHEREUM));
        Assert.False(new ProbeBlockchainNetworkHealth(state).IsDegraded(BlockchainNetwork.ETHEREUM));
    }

    [Fact]
    public void Tron_Keeps_The_Outage_Rule()
    {
        var (state, availability) = Build();
        state.Record(PlatformComponents.BlockchainTron, healthy: false, Threshold);
        Assert.True(availability.IsOpenForNewTransactions(BlockchainNetwork.TRON));

        state.Record(PlatformComponents.BlockchainTron, healthy: false, Threshold);
        state.Record(PlatformComponents.BlockchainTron, healthy: false, Threshold);
        Assert.False(availability.IsOpenForNewTransactions(BlockchainNetwork.TRON));
    }

    [Fact]
    public void Last_Probe_Result_Is_Tracked_Per_Component()
    {
        var state = new PlatformHealthMonitorState();
        Assert.Null(state.LastProbeHealthy(PlatformComponents.BlockchainEthereum));
        state.Record(PlatformComponents.BlockchainEthereum, healthy: false, Threshold);
        Assert.False(state.LastProbeHealthy(PlatformComponents.BlockchainEthereum));
        Assert.Null(state.LastProbeHealthy(PlatformComponents.BlockchainTron));
    }
}
