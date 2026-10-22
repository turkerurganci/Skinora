using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Skinora.Platform.Domain.Entities;
using Skinora.Shared.Blockchain;
using Skinora.Shared.Enums;
using Skinora.Shared.Persistence;

namespace Skinora.Transactions.Application.Networks;

/// <summary>
/// T169 — a network's own minimum price (proje sahibi kararı 2026-10-21: below
/// 50 USDT a sale has no TRON option, only Ethereum). One rule read by every
/// surface that offers or takes a network: listing creation (07 §7.2), the
/// accept endpoint (07 §7.6), the detail's per-network quotes (07 §7.5) and the
/// wizard's form parameters (07 §7.4). Rejections use
/// <c>NETWORK_MIN_AMOUNT_NOT_MET</c> (422, <c>details.network</c> +
/// <c>details.minAmount</c>).
/// </summary>
/// <remarks>
/// Only networks whose <see cref="BlockchainNetworkSettingKeys"/> name a
/// <c>MinTransactionAmountUsdt</c> key carry a minimum (TRON); the others have
/// none. A missing, unconfigured, unparsable or non-positive row reads as the
/// seeded default (<see cref="BlockchainNetworkSettingsProvider.DefaultsFor"/>),
/// so a broken row keeps the rule rather than silently dropping it — the same
/// read envelope as every other per-network setting.
/// </remarks>
public static class NetworkMinimumPrice
{
    /// <summary>Per network, the minimum price (networks without one are absent).</summary>
    public static async Task<IReadOnlyDictionary<BlockchainNetwork, decimal>> ReadAsync(
        AppDbContext db, CancellationToken cancellationToken)
    {
        var keyed = Enum.GetValues<BlockchainNetwork>()
            .Select(n => (Network: n, Key: BlockchainNetworkSettingKeys.For(n).MinTransactionAmountUsdt))
            .Where(x => x.Key is not null)
            .ToList();
        if (keyed.Count == 0) return new Dictionary<BlockchainNetwork, decimal>();

        var wanted = keyed.Select(x => x.Key!).ToList();
        var rows = await db.Set<SystemSetting>()
            .AsNoTracking()
            .Where(s => wanted.Contains(s.Key) && s.IsConfigured)
            .Select(s => new { s.Key, s.Value })
            .ToDictionaryAsync(r => r.Key, r => r.Value, cancellationToken);

        var result = new Dictionary<BlockchainNetwork, decimal>();
        foreach (var (network, key) in keyed)
        {
            var fallback = BlockchainNetworkSettingsProvider.DefaultsFor(network).MinTransactionAmountUsdt;
            if (rows.TryGetValue(key!, out var raw)
                && raw is not null
                && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0m)
            {
                result[network] = parsed;
            }
            else if (fallback is { } value)
            {
                result[network] = value;
            }
        }
        return result;
    }

    /// <summary>
    /// True when <paramref name="price"/> is below <paramref name="network"/>'s
    /// minimum; <paramref name="minimum"/> is then that minimum.
    /// </summary>
    public static bool IsBelow(
        decimal price,
        BlockchainNetwork network,
        IReadOnlyDictionary<BlockchainNetwork, decimal> minimums,
        out decimal minimum)
    {
        if (minimums.TryGetValue(network, out minimum) && price < minimum) return true;
        minimum = 0m;
        return false;
    }

    /// <summary>The error <c>details</c> object (07 §1.3): the network and its minimum.</summary>
    public static object Details(BlockchainNetwork network, decimal minimum) => new
    {
        network = network.ToString(),
        minAmount = minimum.ToString("0.00", CultureInfo.InvariantCulture),
    };
}
