using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;

namespace Skinora.Transactions.Application.Networks;

/// <summary>
/// Live reader of one network's payment parameters (T148 — 05 §3.3a
/// "Ağ başına ayarlar", 06 §3.17). Keys come from
/// <see cref="BlockchainNetworkSettingKeys"/>; a value the network does not
/// have is <c>null</c> in the result, so a caller can never read a TRON number
/// for Ethereum or the other way round.
/// </summary>
public interface IBlockchainNetworkSettingsProvider
{
    Task<BlockchainNetworkSettings> GetAsync(BlockchainNetwork network, CancellationToken cancellationToken);
}

/// <summary>Snapshot of one network's parameters; see <see cref="BlockchainNetworkSettingKeys"/> for meanings.</summary>
public sealed record BlockchainNetworkSettings(
    BlockchainNetwork Network,
    decimal RefundGasFeeFallbackUsdt,
    decimal PayoutGasFeeFallbackUsdt,
    decimal MaxChargedGasFeeUsdt,
    decimal HotWalletNativeBalanceMinimum,
    decimal? FuelWalletNativeBalanceMinimum,
    decimal? FeeCapUsdPerTransfer,
    int? FeeCapWaitAlarmMinutes,
    int? FinalityStallAlarmMinutes,
    decimal MinRefundThresholdRatio,
    decimal? NetworkFeeBufferRatio,
    decimal? NetworkFeeFallbackUsdt,
    decimal? MinTransactionAmountUsdt = null,
    decimal DepositRefundDailyLimitUsdt = BlockchainNetworkSettingsProvider.DefaultDepositRefundDailyLimitUsdt);

/// <summary>
/// Default <see cref="IBlockchainNetworkSettingsProvider"/>. Same read-side
/// envelope as <c>GasFeeSettingsProvider</c>: a missing, unconfigured or
/// non-positive row falls back to the seeded default of that network rather
/// than letting a nonsense value into a money path.
/// </summary>
public sealed class BlockchainNetworkSettingsProvider : IBlockchainNetworkSettingsProvider
{
    /// <summary>
    /// T169a — the deposit refund daily limit both networks seed (proje sahibi
    /// kararı 2026-10-21: 100 $ per network per day); also the read-side
    /// fallback, so a missing or bad row never lifts the limit.
    /// </summary>
    public const decimal DefaultDepositRefundDailyLimitUsdt = 100m;

    /// <summary>Seeded defaults (06 §3.17) — the read-side fallback per network.</summary>
    public static readonly BlockchainNetworkSettings TronDefaults = new(
        Network: BlockchainNetwork.TRON,
        RefundGasFeeFallbackUsdt: 2.80m,
        PayoutGasFeeFallbackUsdt: 2.25m,
        MaxChargedGasFeeUsdt: 10.0m,
        HotWalletNativeBalanceMinimum: 1500m,
        FuelWalletNativeBalanceMinimum: null,
        FeeCapUsdPerTransfer: null,
        FeeCapWaitAlarmMinutes: null,
        FinalityStallAlarmMinutes: null,
        MinRefundThresholdRatio: 2.0m,
        NetworkFeeBufferRatio: 0.007m,
        NetworkFeeFallbackUsdt: 3.00m,
        MinTransactionAmountUsdt: 50m);

    /// <inheritdoc cref="TronDefaults"/>
    public static readonly BlockchainNetworkSettings EthereumDefaults = new(
        Network: BlockchainNetwork.ETHEREUM,
        RefundGasFeeFallbackUsdt: 0.20m,
        PayoutGasFeeFallbackUsdt: 0.50m,
        MaxChargedGasFeeUsdt: 10.0m,
        HotWalletNativeBalanceMinimum: 0.06m,
        FuelWalletNativeBalanceMinimum: 0.06m,
        FeeCapUsdPerTransfer: 0.50m,
        FeeCapWaitAlarmMinutes: 180,
        FinalityStallAlarmMinutes: 30,
        MinRefundThresholdRatio: 2.0m,
        NetworkFeeBufferRatio: null,
        NetworkFeeFallbackUsdt: null);

    private readonly AppDbContext _db;

    public BlockchainNetworkSettingsProvider(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Seeded defaults of <paramref name="network"/>.</summary>
    public static BlockchainNetworkSettings DefaultsFor(BlockchainNetwork network) => network switch
    {
        BlockchainNetwork.TRON => TronDefaults,
        BlockchainNetwork.ETHEREUM => EthereumDefaults,
        _ => throw new ArgumentOutOfRangeException(nameof(network), network, "No settings are defined for this network."),
    };

    public async Task<BlockchainNetworkSettings> GetAsync(
        BlockchainNetwork network, CancellationToken cancellationToken)
    {
        var keys = BlockchainNetworkSettingKeys.For(network);
        var defaults = DefaultsFor(network);
        var wanted = keys.AllKeys();

        var rows = await _db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => wanted.Contains(s.Key) && s.IsConfigured)
            .Select(s => new { s.Key, s.Value })
            .ToDictionaryAsync(r => r.Key, r => r.Value, cancellationToken);

        return new BlockchainNetworkSettings(
            Network: network,
            RefundGasFeeFallbackUsdt: ReadDecimal(rows, keys.RefundGasFeeFallbackUsdt, defaults.RefundGasFeeFallbackUsdt),
            PayoutGasFeeFallbackUsdt: ReadDecimal(rows, keys.PayoutGasFeeFallbackUsdt, defaults.PayoutGasFeeFallbackUsdt),
            MaxChargedGasFeeUsdt: ReadDecimal(rows, keys.MaxChargedGasFeeUsdt, defaults.MaxChargedGasFeeUsdt),
            HotWalletNativeBalanceMinimum: ReadDecimal(rows, keys.HotWalletNativeBalanceMinimum, defaults.HotWalletNativeBalanceMinimum),
            FuelWalletNativeBalanceMinimum: ReadOptionalDecimal(rows, keys.FuelWalletNativeBalanceMinimum, defaults.FuelWalletNativeBalanceMinimum),
            FeeCapUsdPerTransfer: ReadOptionalDecimal(rows, keys.FeeCapUsdPerTransfer, defaults.FeeCapUsdPerTransfer),
            FeeCapWaitAlarmMinutes: ReadOptionalInt(rows, keys.FeeCapWaitAlarmMinutes, defaults.FeeCapWaitAlarmMinutes),
            FinalityStallAlarmMinutes: ReadOptionalInt(rows, keys.FinalityStallAlarmMinutes, defaults.FinalityStallAlarmMinutes),
            MinRefundThresholdRatio: ReadDecimal(rows, keys.MinRefundThresholdRatio, defaults.MinRefundThresholdRatio),
            NetworkFeeBufferRatio: ReadOptionalBufferRatio(rows, keys.NetworkFeeBufferRatio, defaults.NetworkFeeBufferRatio),
            NetworkFeeFallbackUsdt: ReadOptionalDecimal(rows, keys.NetworkFeeFallbackUsdt, defaults.NetworkFeeFallbackUsdt),
            MinTransactionAmountUsdt: ReadOptionalDecimal(rows, keys.MinTransactionAmountUsdt, defaults.MinTransactionAmountUsdt),
            DepositRefundDailyLimitUsdt: ReadDecimal(rows, keys.DepositRefundDailyLimitUsdt, defaults.DepositRefundDailyLimitUsdt));
    }

    /// <summary>
    /// T149 — the network fee buffer is a ratio where 0 is meaningful ("no
    /// head-room"), so unlike the positive-only reads it accepts
    /// <c>0 ≤ value &lt; 1</c> (the validator's envelope); anything else falls
    /// back to the seeded default.
    /// </summary>
    private static decimal? ReadOptionalBufferRatio(
        IReadOnlyDictionary<string, string?> rows, string? key, decimal? fallback)
    {
        if (key is null || fallback is null) return null;
        if (rows.TryGetValue(key, out var raw)
            && raw is not null
            && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 0m
            && parsed < 1m)
        {
            return parsed;
        }
        return fallback;
    }

    private static decimal ReadDecimal(
        IReadOnlyDictionary<string, string?> rows, string key, decimal fallback)
    {
        if (rows.TryGetValue(key, out var raw)
            && raw is not null
            && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0m)
        {
            return parsed;
        }
        return fallback;
    }

    private static decimal? ReadOptionalDecimal(
        IReadOnlyDictionary<string, string?> rows, string? key, decimal? fallback) =>
        key is null || fallback is null ? null : ReadDecimal(rows, key, fallback.Value);

    private static int? ReadOptionalInt(
        IReadOnlyDictionary<string, string?> rows, string? key, int? fallback)
    {
        if (key is null || fallback is null) return null;
        if (rows.TryGetValue(key, out var raw)
            && raw is not null
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0)
        {
            return parsed;
        }
        return fallback;
    }
}
