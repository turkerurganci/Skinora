using System.Collections.Concurrent;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.GasFee;

/// <summary>Which account an estimated transfer leaves from — what its cost depends on most.</summary>
public enum GasFeeSender
{
    /// <summary>The deposit address (refund before the sweep): TRON's activation and burn, Ethereum's fuel.</summary>
    Deposit,

    /// <summary>The network's hot wallet (payout; Ethereum refund after the sweep).</summary>
    HotWallet,
}

/// <summary>
/// T160b (öneriyle kaydedildi — ⚠ PARA) — process-wide memo of the last
/// accepted gas estimate per (network, sender, token): the refund fallback
/// charges it, while it is fresh, instead of the static setting. Singleton.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the last measurement and not the setting.</b> The setting is one
/// number for every moment and every environment: Ethereum's 0.50 is the
/// per-transfer fee cap, about ten times a mainnet refund today (~0.05 $,
/// T142) and below a Sepolia one (0.87, T159b); TRON's 2.00 is below what a
/// deposit refund burns (activation + 64k–130k Energy). The estimate a few
/// hours ago, of the same kind of transfer on the same network, is what the
/// network charged then; the tariff moves far less in that time than the
/// distance between the setting and it. The setting stays the fallback when
/// nothing fresh was measured (a restart, a long outage, a first refund).
/// </para>
/// <para>
/// <b>What is recorded.</b> Only estimates that were charged
/// (<see cref="GasFeeSource.RuntimeEstimate"/>, i.e. below
/// <c>max_charged_gas_fee_usdt</c>) — a refused one is never a measurement.
/// Payout estimates feed the hot-wallet entry (the same transfer shape as a
/// hot-wallet refund); the payout's own fallback is not changed by T160b.
/// What is not in the key — whether the recipient already holds the token
/// (TRON ~64k vs ~130k Energy, Ethereum ~47k vs ~70k gas) and whether the
/// deposit needs fuel first — is a spread around the measured value that is
/// larger than the unit counts suggest: T159b's two Sepolia refunds from one
/// deposit cost 0.87 and 0.18 (≈4.8×). That is why the resolver charges
/// <c>min(measurement, static setting)</c> (T160b B1): the buyer never pays
/// more than the setting charged before T160b.
/// </para>
/// <para>
/// <b>Fresh = <see cref="Ttl"/>, 6 hours.</b> On TRON the unit prices change
/// only by governance and TRX moves slowly; on Ethereum the 30-day median
/// price per gas is 0.18 gwei with p90 1.45 (T142), and a refund above the
/// fee cap waits at the send anyway (08 §3.7.7). At 5–20 sales a day a
/// shorter window would almost never hold a measurement of a deposit refund.
/// In memory only: a restart forgets it and the setting applies until the
/// next estimate succeeds.
/// </para>
/// </remarks>
public sealed class MeasuredGasFeeCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<(BlockchainNetwork, GasFeeSender, StablecoinType), Measurement> _entries = new();
    private readonly TimeProvider _clock;

    public MeasuredGasFeeCache(TimeProvider clock)
    {
        _clock = clock;
    }

    /// <summary>Remember an estimate that was charged.</summary>
    public void Record(BlockchainNetwork network, GasFeeSender sender, StablecoinType token, decimal feeUsdt) =>
        _entries[(network, sender, token)] = new Measurement(feeUsdt, _clock.GetUtcNow());

    /// <summary>The last charged estimate of this kind when it is at most <see cref="Ttl"/> old.</summary>
    public bool TryGetFresh(
        BlockchainNetwork network, GasFeeSender sender, StablecoinType token, out Measurement measurement)
    {
        if (_entries.TryGetValue((network, sender, token), out var entry)
            && _clock.GetUtcNow() - entry.MeasuredAt <= Ttl)
        {
            measurement = entry;
            return true;
        }
        measurement = default!;
        return false;
    }

    /// <summary>Forget everything (tests sharing one host).</summary>
    public void Clear() => _entries.Clear();

    public sealed record Measurement(decimal FeeUsdt, DateTimeOffset MeasuredAt);
}
