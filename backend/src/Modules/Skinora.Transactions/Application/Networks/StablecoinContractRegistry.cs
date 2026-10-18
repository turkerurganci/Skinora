using Microsoft.Extensions.Options;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.Transactions.Application.Networks;

/// <summary>
/// Token contracts per network (T148 — 05 §3.3a "Ağ başına ayarlar"). A
/// TRON deposit is watched for the TRC-20 USDT contract, an Ethereum deposit
/// for the ERC-20 one (08 §3.7.1); arming a monitor with the other network's
/// contract would classify every real payment as a wrong token and route it to
/// a refund — the same silent failure the testnet override fixed for TRON
/// (<see cref="StablecoinContractOptions"/> remarks).
/// </summary>
public interface IStablecoinContractRegistry
{
    /// <summary>Contract address of <paramref name="token"/> on <paramref name="network"/>.</summary>
    string ResolveContractAddress(BlockchainNetwork network, StablecoinType token);

    /// <summary>
    /// The allowlisted token whose contract on <paramref name="network"/> is
    /// <paramref name="contractAddress"/>; <c>null</c> for anything else
    /// (spam token, 08 §3.4).
    /// </summary>
    StablecoinType? ResolveByContract(BlockchainNetwork network, string? contractAddress);
}

/// <summary>
/// Default <see cref="IStablecoinContractRegistry"/>: TRON reads the
/// <c>StablecoinContracts</c> section (default options instance, unchanged),
/// Ethereum the <c>EthereumStablecoinContracts</c> section (named instance
/// <c>"ETHEREUM"</c>); an unset value falls back to that network's mainnet
/// contract.
/// </summary>
public sealed class StablecoinContractRegistry : IStablecoinContractRegistry
{
    private readonly Func<BlockchainNetwork, StablecoinContractOptions> _optionsFor;

    public StablecoinContractRegistry(IOptionsMonitor<StablecoinContractOptions> options)
    {
        _optionsFor = network => options.Get(OptionsNameFor(network));
    }

    private StablecoinContractRegistry(Func<BlockchainNetwork, StablecoinContractOptions> optionsFor)
    {
        _optionsFor = optionsFor;
    }

    /// <summary>
    /// Registry over fixed option instances — composition outside the container
    /// (tests, tooling). A network without an instance resolves its mainnet
    /// contracts.
    /// </summary>
    public static StablecoinContractRegistry FromOptions(
        StablecoinContractOptions tron, StablecoinContractOptions? ethereum = null)
    {
        var eth = ethereum ?? new StablecoinContractOptions();
        return new StablecoinContractRegistry(network => network switch
        {
            BlockchainNetwork.TRON => tron,
            _ => eth,
        });
    }

    /// <summary>Options-instance name of <paramref name="network"/> (TRON = default instance).</summary>
    public static string OptionsNameFor(BlockchainNetwork network) =>
        network == BlockchainNetwork.TRON ? Options.DefaultName : network.ToString();

    /// <summary>Configuration section of <paramref name="network"/>'s contracts.</summary>
    public static string SectionNameFor(BlockchainNetwork network) => network switch
    {
        BlockchainNetwork.TRON => StablecoinContractOptions.SectionName,
        BlockchainNetwork.ETHEREUM => StablecoinContractOptions.EthereumSectionName,
        _ => throw new ArgumentOutOfRangeException(nameof(network), network, "No contract section for this network."),
    };

    public string ResolveContractAddress(BlockchainNetwork network, StablecoinType token) =>
        _optionsFor(network).ResolveContractAddress(token, network);

    public StablecoinType? ResolveByContract(BlockchainNetwork network, string? contractAddress) =>
        _optionsFor(network).ResolveByContract(contractAddress, network);
}
