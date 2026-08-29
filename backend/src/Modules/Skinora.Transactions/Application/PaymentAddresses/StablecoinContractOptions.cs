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
    public const string SectionName = "StablecoinContracts";

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
    public string ResolveContractAddress(StablecoinType token) => token switch
    {
        StablecoinType.USDT => Fallback(Usdt, KnownStablecoinContracts.Usdt),
        StablecoinType.USDC => Fallback(Usdc, KnownStablecoinContracts.Usdc),
        _ => throw new ArgumentOutOfRangeException(nameof(token), token, "Unsupported stablecoin."),
    };

    /// <summary>
    /// Reverse lookup used by the webhook validator to name the token a
    /// deposit arrived in. Returns <c>null</c> for anything outside the
    /// allowlist (spam token, 08 §3.4).
    /// </summary>
    public StablecoinType? ResolveByContract(string? contractAddress)
    {
        if (string.IsNullOrWhiteSpace(contractAddress)) return null;

        // Contract-address equality without case-folding: Tron addresses ship
        // as case-sensitive base58 (T70 derivation).
        if (contractAddress.Equals(ResolveContractAddress(StablecoinType.USDT), StringComparison.Ordinal))
            return StablecoinType.USDT;
        if (contractAddress.Equals(ResolveContractAddress(StablecoinType.USDC), StringComparison.Ordinal))
            return StablecoinType.USDC;

        return null;
    }

    private static string Fallback(string configured, string mainnetDefault) =>
        string.IsNullOrWhiteSpace(configured) ? mainnetDefault : configured;
}
