using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Networks;
using Skinora.Transactions.Application.PaymentAddresses;
using Skinora.Transactions.Application.Webhooks;

namespace Skinora.Transactions.Tests.Unit.PaymentAddresses;

/// <summary>
/// T148 (05 §3.3a, 08 §3.7.1) — token contracts are per network. A deposit is
/// armed with ITS network's USDT contract; arming an Ethereum deposit with the
/// TRC-20 address would make every real payment look like a wrong token.
/// </summary>
public class StablecoinContractRegistryTests
{
    private const string NileUsdt = "TXYZopYRdj2D9XRtbG411XZZ3kM5VkAeBf";
    private const string SepoliaUsdtChecksummed = "0x1c7D4B196Cb0C7B01d743Fbc6116a902379C7238";

    [Fact]
    public void Unconfigured_Networks_Resolve_Their_Own_Mainnet_Contracts()
    {
        var sut = StablecoinContractRegistry.FromOptions(new StablecoinContractOptions());

        Assert.Equal(KnownStablecoinContracts.Usdt, sut.ResolveContractAddress(BlockchainNetwork.TRON, StablecoinType.USDT));
        Assert.Equal("0xdac17f958d2ee523a2206206994597c13d831ec7",
            sut.ResolveContractAddress(BlockchainNetwork.ETHEREUM, StablecoinType.USDT));
        Assert.Equal("0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48",
            sut.ResolveContractAddress(BlockchainNetwork.ETHEREUM, StablecoinType.USDC));
    }

    [Fact]
    public void A_Contract_Is_Recognised_Only_On_Its_Own_Network()
    {
        var sut = StablecoinContractRegistry.FromOptions(new StablecoinContractOptions());

        // EIP-55 spelling of Ethereum USDT matches on Ethereum (canonical compare)...
        Assert.Equal(StablecoinType.USDT,
            sut.ResolveByContract(BlockchainNetwork.ETHEREUM, "0xdAC17F958D2ee523a2206206994597C13D831ec7"));
        // ...but is a spam contract on TRON, and TRON's USDT is one on Ethereum.
        Assert.Null(sut.ResolveByContract(BlockchainNetwork.TRON, "0xdAC17F958D2ee523a2206206994597C13D831ec7"));
        Assert.Null(sut.ResolveByContract(BlockchainNetwork.ETHEREUM, KnownStablecoinContracts.Usdt));
        Assert.Equal(StablecoinType.USDT, sut.ResolveByContract(BlockchainNetwork.TRON, KnownStablecoinContracts.Usdt));
    }

    [Fact]
    public void Each_Network_Reads_Its_Own_Configuration_Section()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["StablecoinContracts:Usdt"] = NileUsdt,
            ["EthereumStablecoinContracts:Usdt"] = SepoliaUsdtChecksummed,
        }).Build();
        var services = new ServiceCollection();
        foreach (var network in Enum.GetValues<BlockchainNetwork>())
        {
            services.Configure<StablecoinContractOptions>(
                StablecoinContractRegistry.OptionsNameFor(network),
                configuration.GetSection(StablecoinContractRegistry.SectionNameFor(network)));
        }
        using var provider = services.BuildServiceProvider();
        var sut = new StablecoinContractRegistry(
            provider.GetRequiredService<IOptionsMonitor<StablecoinContractOptions>>());

        Assert.Equal(NileUsdt, sut.ResolveContractAddress(BlockchainNetwork.TRON, StablecoinType.USDT));
        // Canonical lowercase on Ethereum (06 §1.4).
        Assert.Equal(SepoliaUsdtChecksummed.ToLowerInvariant(),
            sut.ResolveContractAddress(BlockchainNetwork.ETHEREUM, StablecoinType.USDT));
        // The TRON default instance is still what a pre-F8 IOptions reader sees.
        Assert.Equal(NileUsdt,
            provider.GetRequiredService<IOptions<StablecoinContractOptions>>().Value.ResolveContractAddress(StablecoinType.USDT));
    }
}
