namespace Skinora.Platform.Application.Wallets;

/// <summary>
/// The platform's own Tron addresses — the hot wallet deposits are swept into
/// and the cold wallet operational consolidation is sent to (05 §3.3).
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
/// read-only, and the sidecar refuses to sign to anything else.
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
    /// <summary>Sweep destination and reconciliation hot-wallet scope; null when unset.</summary>
    string? HotWalletAddress { get; }

    /// <summary>Consolidation destination and reconciliation cold-wallet scope; null when unset.</summary>
    string? ColdWalletAddress { get; }
}
