using Skinora.Shared.Enums;

namespace Skinora.Shared.Blockchain;

/// <summary>
/// <c>SystemSetting</c> keys of the per-network payment parameters (T148 —
/// 05 §3.3a "Ağ başına ayarlar", 06 §1.4 "Ayarlar", 06 §3.17). One instance per
/// network; <see cref="For"/> picks it.
/// </summary>
/// <remarks>
/// <para>
/// <b>TRON keeps its existing key names</b> (öneriyle kaydedildi — ⚠): the rows
/// predate the network dimension, admins may already have tuned them, and a
/// rename would move every value through a data migration for no behavioural
/// gain. Ethereum's keys carry the <c>blockchain.ethereum.</c> prefix. A value
/// the network does not have (TRON has no fuel wallet, no fee cap, no
/// <c>finalized</c> checkpoint) is <c>null</c> here, so a caller cannot read a
/// TRON number for Ethereum or the other way round.
/// </para>
/// <para>
/// Not here, on purpose: the platform's hot / cold addresses and the
/// single / daily send caps are deployment configuration of each network's
/// service, never admin-editable rows (05 §3.3 "Platform adresleri",
/// 02 §16.2) — see <c>IPlatformWalletAddressProvider</c> and the services'
/// own <c>MAX_SINGLE_TRANSFER_USDT</c> / <c>MAX_DAILY_OUTFLOW_USDT</c>. The
/// confirmation rule is a chain fact (<see cref="BlockchainNetworkPolicy"/>).
/// T149 added the minimum-refund multiplier (TRON keeps the unprefixed
/// <c>min_refund_threshold_ratio</c>) and TRON's network fee line settings
/// (buffer and fallback — <c>blockchain.tron.*</c>, new keys; Ethereum has no
/// line).
/// </para>
/// </remarks>
/// <param name="Network">The network these keys belong to.</param>
/// <param name="RefundGasFeeFallbackUsdt">Refund fee charged when the runtime estimate is unavailable.</param>
/// <param name="PayoutGasFeeFallbackUsdt">Payout fee used in the protection split when the estimate is unavailable.</param>
/// <param name="MaxChargedGasFeeUsdt">Sanity ceiling on a runtime estimate (refused above it, not clamped).</param>
/// <param name="HotWalletNativeBalanceMinimum">Hot wallet gas-asset floor (TRX / ETH) — below it an admin alert.</param>
/// <param name="FuelWalletNativeBalanceMinimum">Fuel wallet ETH floor (08 §3.7.5); Ethereum only.</param>
/// <param name="FeeCapUsdPerTransfer">Platform-paid fee cap per transfer in USD (08 §3.7.7); Ethereum only.</param>
/// <param name="FeeCapWaitAlarmMinutes">Uninterrupted wait above the cap before an admin alarm (08 §3.7.7); Ethereum only.</param>
/// <param name="FinalityStallAlarmMinutes">Age of the last <c>finalized</c> block that raises an alarm (08 §3.7.8); Ethereum only.</param>
/// <param name="MinRefundThresholdRatio">Minimum-refund multiplier: a refund whose net is below <c>gasFee × ratio</c> is not sent (02 §4.6, T149).</param>
/// <param name="NetworkFeeBufferRatio">TRX price head-room added to the buyer's network fee line (11 §F8 K8, T149); TRON only.</param>
/// <param name="NetworkFeeFallbackUsdt">Network fee line written when the sweep estimate is unavailable at acceptance (T149); TRON only.</param>
public sealed record BlockchainNetworkSettingKeys(
    BlockchainNetwork Network,
    string RefundGasFeeFallbackUsdt,
    string PayoutGasFeeFallbackUsdt,
    string MaxChargedGasFeeUsdt,
    string HotWalletNativeBalanceMinimum,
    string? FuelWalletNativeBalanceMinimum,
    string? FeeCapUsdPerTransfer,
    string? FeeCapWaitAlarmMinutes,
    string? FinalityStallAlarmMinutes,
    string MinRefundThresholdRatio,
    string? NetworkFeeBufferRatio,
    string? NetworkFeeFallbackUsdt)
{
    /// <summary>TRON — the pre-F8 keys, unchanged.</summary>
    public static readonly BlockchainNetworkSettingKeys Tron = new(
        Network: BlockchainNetwork.TRON,
        RefundGasFeeFallbackUsdt: "blockchain.refund_gas_fee_estimate_usdt",
        PayoutGasFeeFallbackUsdt: "blockchain.payout_gas_fee_estimate_usdt",
        MaxChargedGasFeeUsdt: "blockchain.max_charged_gas_fee_usdt",
        HotWalletNativeBalanceMinimum: "hot_wallet.trx_balance_minimum",
        FuelWalletNativeBalanceMinimum: null,
        FeeCapUsdPerTransfer: null,
        FeeCapWaitAlarmMinutes: null,
        FinalityStallAlarmMinutes: null,
        MinRefundThresholdRatio: "min_refund_threshold_ratio",
        NetworkFeeBufferRatio: "blockchain.tron.network_fee_buffer_ratio",
        NetworkFeeFallbackUsdt: "blockchain.tron.network_fee_fallback_usdt");

    /// <summary>Ethereum — T148 keys, seeded with the T142 measurements.</summary>
    public static readonly BlockchainNetworkSettingKeys Ethereum = new(
        Network: BlockchainNetwork.ETHEREUM,
        RefundGasFeeFallbackUsdt: "blockchain.ethereum.refund_gas_fee_estimate_usdt",
        PayoutGasFeeFallbackUsdt: "blockchain.ethereum.payout_gas_fee_estimate_usdt",
        MaxChargedGasFeeUsdt: "blockchain.ethereum.max_charged_gas_fee_usdt",
        HotWalletNativeBalanceMinimum: "blockchain.ethereum.hot_wallet_eth_balance_minimum",
        FuelWalletNativeBalanceMinimum: "blockchain.ethereum.fuel_wallet_eth_balance_minimum",
        FeeCapUsdPerTransfer: "blockchain.ethereum.fee_cap_usd_per_transfer",
        FeeCapWaitAlarmMinutes: "blockchain.ethereum.fee_cap_wait_alarm_minutes",
        FinalityStallAlarmMinutes: "blockchain.ethereum.finality_stall_alarm_minutes",
        MinRefundThresholdRatio: "blockchain.ethereum.min_refund_threshold_ratio",
        NetworkFeeBufferRatio: null,
        NetworkFeeFallbackUsdt: null);

    /// <summary>The keys of <paramref name="network"/>.</summary>
    public static BlockchainNetworkSettingKeys For(BlockchainNetwork network) => network switch
    {
        BlockchainNetwork.TRON => Tron,
        BlockchainNetwork.ETHEREUM => Ethereum,
        _ => throw new ArgumentOutOfRangeException(
            nameof(network), network, "No per-network setting keys are defined for this network."),
    };

    /// <summary>Every key of this network that has a value (nulls skipped).</summary>
    public IReadOnlyList<string> AllKeys() =>
        new[]
        {
            RefundGasFeeFallbackUsdt,
            PayoutGasFeeFallbackUsdt,
            MaxChargedGasFeeUsdt,
            HotWalletNativeBalanceMinimum,
            FuelWalletNativeBalanceMinimum,
            FeeCapUsdPerTransfer,
            FeeCapWaitAlarmMinutes,
            FinalityStallAlarmMinutes,
            MinRefundThresholdRatio,
            NetworkFeeBufferRatio,
            NetworkFeeFallbackUsdt,
        }
        .Where(k => k is not null)
        .Select(k => k!)
        .ToList();
}
