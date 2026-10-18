using System.Collections.Concurrent;
using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.GasFee;

/// <summary>
/// T149 — process-wide memo of the last sweep cost estimate per
/// (network, token), so the network fee a buyer is shown in
/// <c>networkQuotes</c> and the line acceptance fixes a few minutes later come
/// from the same estimate (öneriyle kaydedildi — ⚠). Singleton.
/// </summary>
/// <remarks>
/// <para>
/// Only the raw estimate (SUN + TRX price) is cached, never the line: the
/// buffer and fallback settings are read on every call, so an admin change
/// applies at once.
/// </para>
/// <para>
/// <see cref="EstimateTtl"/> matches the sidecar's own TRX price cache
/// (5 min, 08 §3.1a step 5) — a fresher estimate would carry the same price.
/// A failed estimate is remembered for <see cref="FailureTtl"/> only, so an
/// outage does not make every detail view wait on a dead service while a
/// recovered service is asked again within a minute.
/// </para>
/// </remarks>
public sealed class SweepCostEstimateCache
{
    public static readonly TimeSpan EstimateTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<(BlockchainNetwork, StablecoinType), Entry> _entries = new();
    private readonly TimeProvider _clock;

    public SweepCostEstimateCache(TimeProvider clock)
    {
        _clock = clock;
    }

    /// <summary>
    /// The cached answer for the key when still fresh: <c>found = true</c> and
    /// the estimate (which is <c>null</c> for a remembered failure).
    /// </summary>
    public bool TryGet(BlockchainNetwork network, StablecoinType token, out SweepCostEstimate? estimate)
    {
        if (_entries.TryGetValue((network, token), out var entry) && entry.ExpiresAt > _clock.GetUtcNow())
        {
            estimate = entry.Estimate;
            return true;
        }
        estimate = null;
        return false;
    }

    /// <summary>Remember <paramref name="estimate"/> (<c>null</c> = the estimate failed).</summary>
    public void Set(BlockchainNetwork network, StablecoinType token, SweepCostEstimate? estimate)
    {
        var ttl = estimate is null ? FailureTtl : EstimateTtl;
        _entries[(network, token)] = new Entry(estimate, _clock.GetUtcNow() + ttl);
    }

    /// <summary>Forget everything (tests sharing one host).</summary>
    public void Clear() => _entries.Clear();

    private sealed record Entry(SweepCostEstimate? Estimate, DateTimeOffset ExpiresAt);
}
