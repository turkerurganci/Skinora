using Skinora.Shared.Enums;

namespace Skinora.Platform.Application.Wallets;

/// <summary>
/// The platform's own addresses on each network — the hot wallet deposits are
/// swept into and the cold wallet operational consolidation is sent to
/// (05 §3.3). Every network has its own pair (11 §F8 K3, 05 §3.3a "Ağ başına
/// ayarlar"); a network's balance never pays another network's transfer.
/// </summary>
/// <remarks>
/// <para>
/// These used to be <c>SystemSetting</c> rows (<c>reconciliation.hot_wallet_address</c>,
/// <c>reconciliation.cold_wallet_address</c>) any holder of <c>MANAGE_SETTINGS</c>
/// could rewrite from the admin panel, with no format check, no cooldown and no
/// second approval — while the sidecar signed whatever destination arrived in
/// the request. Rewriting either one redirected real money: the cold address
/// drained the hot wallet through AD20, the hot address diverted every sweep of
/// a settled sale. Owner decision 2026-09-16: both addresses are deployment
/// configuration read from the environment, the admin panel shows them
/// read-only, and the signing service refuses to sign to anything else.
/// </para>
/// <para>
/// A value of <c>null</c> means "not configured" and every caller degrades the
/// same way it did with the old <c>NONE</c> sentinel: the sweep queue skips the
/// run, reconciliation skips that scope, the monitor exits cleanly and the
/// cold transfer endpoint answers <c>COLD_WALLET_NOT_CONFIGURED</c>.
/// </para>
/// </remarks>
public interface IPlatformWalletAddressProvider
{
    /// <summary>
    /// TRON's sweep destination and reconciliation hot-wallet scope; null when
    /// unset. Kept from before the network dimension (T148) — new code asks
    /// <see cref="For"/> with the record's network.
    /// </summary>
    string? HotWalletAddress { get; }

    /// <summary>
    /// TRON's consolidation destination and reconciliation cold-wallet scope;
    /// null when unset. See <see cref="HotWalletAddress"/>.
    /// </summary>
    string? ColdWalletAddress { get; }

    /// <summary>
    /// The pair of <paramref name="network"/> (T148). The default answers TRON
    /// from the two properties above and reports every other network as
    /// unconfigured — the safe reading for a host that only knows TRON (tests,
    /// tooling); <c>EnvPlatformWalletAddressProvider</c> reads each network's
    /// own environment variables.
    /// </summary>
    PlatformWalletAddresses For(BlockchainNetwork network) =>
        network == BlockchainNetwork.TRON
            ? new PlatformWalletAddresses(network, HotWalletAddress, ColdWalletAddress)
            : new PlatformWalletAddresses(network, null, null);
}

/// <summary>One network's platform wallet pair; either address may be unset (<c>null</c>).</summary>
public sealed record PlatformWalletAddresses(
    BlockchainNetwork Network,
    string? HotWalletAddress,
    string? ColdWalletAddress);
