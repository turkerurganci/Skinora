using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;

namespace Skinora.Shared.Tests.Unit.Blockchain;

/// <summary>
/// T148 (05 §3.3a, 06 §1.4 "Onay alanları") — the chain facts that used to be
/// TRON constants scattered through the backend (20 confirmations, SUN,
/// Energy fields) answered per network.
/// </summary>
public class BlockchainNetworkPolicyTests
{
    [Fact]
    public void Every_Network_Has_A_Policy_For_Itself()
    {
        foreach (var network in Enum.GetValues<BlockchainNetwork>())
            Assert.Equal(network, BlockchainNetworkPolicy.For(network).Network);
    }

    [Fact]
    public void Tron_Is_Today_20_Blocks_Trx_In_Sun_With_Energy_Receipts()
    {
        var tron = BlockchainNetworkPolicy.For(BlockchainNetwork.TRON);

        Assert.Equal(ConfirmationRule.BlockCount, tron.Confirmation);
        Assert.Equal(20, tron.RequiredConfirmations);
        Assert.Equal("TRX", tron.NativeAssetSymbol);
        Assert.Equal(6, tron.NativeAssetDecimals);
        Assert.Equal("SUN", tron.NativeFeeUnit);
        Assert.True(tron.ReportsTronResourceUsage);
        Assert.False(tron.ReportsFeeInWei);
    }

    [Fact]
    public void Ethereum_Waits_For_Finalized_In_Wei_Without_Energy()
    {
        var eth = BlockchainNetworkPolicy.For(BlockchainNetwork.ETHEREUM);

        Assert.Equal(ConfirmationRule.Finalized, eth.Confirmation);
        Assert.Null(eth.RequiredConfirmations);
        Assert.Equal("ETH", eth.NativeAssetSymbol);
        Assert.Equal(18, eth.NativeAssetDecimals);
        Assert.Equal("wei", eth.NativeFeeUnit);
        Assert.False(eth.ReportsTronResourceUsage);
        Assert.True(eth.ReportsFeeInWei);
    }

    [Theory]
    [InlineData(BlockchainNetwork.TRON, 19, null, false)]
    [InlineData(BlockchainNetwork.TRON, 20, null, true)]
    [InlineData(BlockchainNetwork.TRON, 20, false, true)]   // TRON ignores the flag
    [InlineData(BlockchainNetwork.TRON, null, true, false)] // ...either way
    [InlineData(BlockchainNetwork.ETHEREUM, 500, null, false)]  // no block count qualifies (K5)
    [InlineData(BlockchainNetwork.ETHEREUM, 500, false, false)]
    [InlineData(BlockchainNetwork.ETHEREUM, 0, true, true)]
    public void IsFinal_Follows_The_Networks_Rule(
        BlockchainNetwork network, int? confirmations, bool? finalized, bool expected)
    {
        Assert.Equal(expected, BlockchainNetworkPolicy.For(network).IsFinal(confirmations, finalized));
    }

    [Theory]
    [InlineData(BlockchainNetwork.TRON, null, 20)]
    [InlineData(BlockchainNetwork.TRON, 5, 20)]
    [InlineData(BlockchainNetwork.TRON, 37, 37)]
    [InlineData(BlockchainNetwork.ETHEREUM, null, 0)]
    [InlineData(BlockchainNetwork.ETHEREUM, 3, 3)]
    [InlineData(BlockchainNetwork.ETHEREUM, -1, 0)]
    public void RecordedConfirmationCount_Applies_Trons_Floor_Only_To_Tron(
        BlockchainNetwork network, int? reported, int stored)
    {
        Assert.Equal(stored, BlockchainNetworkPolicy.For(network).RecordedConfirmationCount(reported));
    }

    [Fact]
    public void Unknown_Network_Value_Has_No_Policy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BlockchainNetworkPolicy.For((BlockchainNetwork)99));
    }
}
