using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Networks;

/// <summary>
/// How the oldest-first, bounded background jobs (outbound dispatch, outbound
/// confirmation, deposit address allocation) share one run between networks
/// (T150 — 11 §F8 T150 "Yapılandırılmış ama erişilemeyen ağ ortak kuyrukları
/// tıkamaz"; T148 validation N1–N2).
/// </summary>
/// <remarks>
/// <para>
/// T148 kept an <em>unconfigured</em> network's rows out of these jobs. A
/// network that is configured but whose service is down still got in: its rows
/// sat at the head of one shared oldest-first batch and every one of them
/// waited for a timeout, so the other network's rows waited too. Three rules
/// close that:
/// </para>
/// <list type="number">
///   <item><b>Reachable networks only.</b> Candidates come from networks that
///     are configured AND not in a detected outage (the platform health probe's
///     reading, <see cref="IBlockchainNetworkHealth"/> — the same signal that
///     freezes that network's payment timeouts). Rows of a network in an outage
///     are not touched: no retry budget spent, nothing flipped to FAILED.</item>
///   <item><b>Each network its own batch.</b> Every reachable network's oldest
///     rows are selected separately (each up to the job's batch size) and
///     processed round-robin, so no network's backlog takes another's slots.</item>
///   <item><b>Stop at the first unreachable answer.</b> Within one run, the
///     first transport-level failure of a network's service (connection,
///     timeout) ends that network's turn for the run
///     (<see cref="NetworkRunBreaker"/>); the probe needs a few minutes to
///     declare the outage, and until then a down service costs one timeout per
///     run, not one per row.</item>
/// </list>
/// </remarks>
public static class NetworkWorkQueue
{
    /// <summary>Configured networks that are not in a detected outage, in enum order.</summary>
    public static IReadOnlyList<BlockchainNetwork> ReachableNetworks(
        this IBlockchainServiceRegistry services,
        IBlockchainNetworkHealth health) =>
        services.ConfiguredNetworks.Where(n => !health.IsDegraded(n)).ToList();

    /// <summary>
    /// Round-robin merge of per-network candidate lists: first of each, then
    /// second of each, … — each list keeps its own (oldest-first) order.
    /// </summary>
    public static IReadOnlyList<T> Interleave<T>(IEnumerable<IReadOnlyList<T>> perNetwork)
    {
        var lists = perNetwork.Where(l => l.Count > 0).ToList();
        var merged = new List<T>(lists.Sum(l => l.Count));
        for (var i = 0; merged.Count < merged.Capacity; i++)
        {
            foreach (var list in lists)
            {
                if (i < list.Count) merged.Add(list[i]);
            }
        }
        return merged;
    }
}

/// <summary>
/// Networks whose service answered "unreachable" during the current job run;
/// the rest of their candidates wait for the next run (see
/// <see cref="NetworkWorkQueue"/>).
/// </summary>
public sealed class NetworkRunBreaker
{
    private readonly HashSet<BlockchainNetwork> _tripped = new();

    public bool IsTripped(BlockchainNetwork network) => _tripped.Contains(network);

    /// <summary>Returns true the first time a network trips in this run.</summary>
    public bool Trip(BlockchainNetwork network) => _tripped.Add(network);
}
