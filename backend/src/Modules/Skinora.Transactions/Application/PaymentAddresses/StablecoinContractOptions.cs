using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
using Skinora.Transactions.Application.Webhooks;

namespace Skinora.Transactions.Application.PaymentAddresses;

/// <summary>
/// Binding target for the <c>StablecoinContracts</c> configuration section —
/// the TRC-20 contract addresses the platform accepts payment on (08 §3.3).
/// Mirrors the sidecar's network-keyed <c>TOKEN_CONTRACTS</c> block
/// (<c>sidecar-blockchain/src/config/index.ts</c>), which hardcodes mainnet and
/// reads testnet addresses from <c>TRON_USDT_CONTRACT</c> /
/// <c>TRON_USDC_CONTRACT</c>.
/// </summary>
/// <remarks>
/// <para>
/// Before this section existed the backend resolved contracts from the
/// <see cref="KnownStablecoinContracts"/> mainnet constants only. On a testnet
/// deployment that made the two halves of the payment path disagree: the
/// backend armed the monitor with the <em>mainnet</em> USDT address while the
/// sidecar watched the network's own. A real buyer payment then matched the
/// sidecar's allowlist but not <c>expectedContract</c>, so
/// <c>classifyToken</c> returned <c>wrong_token</c> and the deposit was routed
/// to auto-refund instead of confirming the transaction — the failure was
/// silent on the backend side because no exception is thrown for a
/// wrong-token deposit.
/// </para>
/// <para>
/// An unset (or whitespace) value falls back to the mainnet constant rather
/// than throwing. Mainnet is the deployment where a wrong address does real
/// financial damage, so it stays the default that needs no configuration;
/// testnets opt in explicitly through <c>StablecoinContracts__Usdt</c>.
/// </para>
/// </remarks>
public sealed class StablecoinContractOptions
{
    /// <summary>TRON's section (pre-F8 name, kept).</summary>
    public const string SectionName = "StablecoinContracts";

    /// <summary>
    /// Ethereum's section (T148), bound as the named options instance
    /// <c>"ETHEREUM"</c>. Empty values fall back to the Ethereum mainnet
    /// contracts (<see cref="KnownStablecoinContracts.EthereumUsdt"/>); a
    /// Sepolia deployment sets its own copy of the token (08 §3.7.1, T159).
    /// </summary>
    public const string EthereumSectionName = "EthereumStablecoinContracts";

    /// <summary>
    /// TRC-20 contract address for USDT on the target network. Empty means
    /// "use the mainnet constant" (see remarks).
    /// </summary>
    public string Usdt { get; set; } = string.Empty;

    /// <summary>
    /// TRC-20 contract address for USDC on the target network. Empty means
    /// "use the mainnet constant" (see remarks). Deliberately blank on Nile —
    /// the allowlist carries USDT only until a testnet USDC address is
    /// resolved from a faucet.
    /// </summary>
    public string Usdc { get; set; } = string.Empty;

    /// <summary>
    /// Canonical contract address for a backend stablecoin enum value. Same
    /// contract as <see cref="KnownStablecoinContracts.ResolveContractAddress"/>,
    /// but network-aware.
    /// </summary>
    public string ResolveContractAddress(StablecoinType token) =>
        ResolveContractAddress(token, BlockchainNetwork.TRON);

    /// <summary>
    /// Contract address of <paramref name="token"/> when this options instance
    /// describes <paramref name="network"/> (T148): a configured value wins,
    /// otherwise that network's mainnet constant. Ethereum values come back in
    /// the canonical lowercase form (06 §1.4).
    /// </summary>
    public string ResolveContractAddress(StablecoinType token, BlockchainNetwork network)
    {
        var configured = token switch
        {
            StablecoinType.USDT => Usdt,
            StablecoinType.USDC => Usdc,
            _ => throw new ArgumentOutOfRangeException(nameof(token), token, "Unsupported stablecoin."),
        };
        return BlockchainAddress.Canonicalize(
            Fallback(configured, KnownStablecoinContracts.ResolveContractAddress(network, token)));
    }

    /// <summary>
    /// Reverse lookup used by the webhook validator to name the token a
    /// deposit arrived in. Returns <c>null</c> for anything outside the
    /// allowlist (spam token, 08 §3.4).
    /// </summary>
    public StablecoinType? ResolveByContract(string? contractAddress) =>
        ResolveByContract(contractAddress, BlockchainNetwork.TRON);

    /// <summary>Network-aware <see cref="ResolveByContract(string?)"/> (T148).</summary>
    public StablecoinType? ResolveByContract(string? contractAddress, BlockchainNetwork network)
    {
        if (string.IsNullOrWhiteSpace(contractAddress)) return null;

        // Canonical equality (06 §1.4, T145): TRON base58 contracts compare
        // exactly — base58 is case-significant — while an EVM contract matches
        // in any letter case (EIP-55 checksum case vs. the lowercase stored
        // form).
        if (BlockchainAddress.AreEqual(contractAddress, ResolveContractAddress(StablecoinType.USDT, network)))
            return StablecoinType.USDT;
        if (BlockchainAddress.AreEqual(contractAddress, ResolveContractAddress(StablecoinType.USDC, network)))
            return StablecoinType.USDC;

        return null;
    }

    private static string Fallback(string configured, string mainnetDefault) =>
        string.IsNullOrWhiteSpace(configured) ? mainnetDefault : configured;
}
