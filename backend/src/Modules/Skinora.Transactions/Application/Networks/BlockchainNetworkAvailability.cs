using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Networks;

/// <summary>
/// Health of each network's service as the platform health probe last saw it
/// (T148 — 05 §3.3a "Sağlık ve dondurma"). Implemented in the API host over the
/// probe's in-memory state; the Transactions module only reads it.
/// </summary>
public interface IBlockchainNetworkHealth
{
    /// <summary>
    /// True while <paramref name="network"/>'s service is in a detected outage
    /// (the probe crossed its failure threshold and has not seen it recover).
    /// </summary>
    bool IsDegraded(BlockchainNetwork network);

    /// <summary>
    /// True when <paramref name="network"/>'s service may be offered for a NEW
    /// transaction (07 §7.4 <c>supportedNetworks</c>). Defaults to "not
    /// degraded"; the probe-backed implementation also asks a network whose
    /// service must declare it can see payments for a positive last probe
    /// (T151 kod düzeyi kapı — Ethereum's <c>acceptsPayments</c>).
    /// </summary>
    bool AcceptsNewTransactions(BlockchainNetwork network) => !IsDegraded(network);
}

/// <summary>
/// Which networks are open for a new transaction right now (07 §1.3
/// <c>supportedNetworks</c>, 05 §3.3a): a network needs a configured service
/// and must not be in a health freeze. A frozen network is closed for new
/// transactions while its active ones wait with their timeouts frozen; the
/// other network stays open (11 §6.2 FK8-6).
/// </summary>
public interface IBlockchainNetworkAvailability
{
    /// <summary>Open networks, in enum order (TRON first).</summary>
    IReadOnlyList<BlockchainNetwork> OpenForNewTransactions();

    /// <summary>True when <paramref name="network"/> is open for a new transaction.</summary>
    bool IsOpenForNewTransactions(BlockchainNetwork network);
}

/// <summary>Default <see cref="IBlockchainNetworkAvailability"/>.</summary>
public sealed class BlockchainNetworkAvailability : IBlockchainNetworkAvailability
{
    private readonly IBlockchainServiceRegistry _services;
    private readonly IBlockchainNetworkHealth _health;

    public BlockchainNetworkAvailability(
        IBlockchainServiceRegistry services,
        IBlockchainNetworkHealth health)
    {
        _services = services;
        _health = health;
    }

    public IReadOnlyList<BlockchainNetwork> OpenForNewTransactions() =>
        Enum.GetValues<BlockchainNetwork>().Where(IsOpenForNewTransactions).ToList();

    public bool IsOpenForNewTransactions(BlockchainNetwork network) =>
        _services.IsConfigured(network) && _health.AcceptsNewTransactions(network);
}

/// <summary>
/// <see cref="IBlockchainNetworkHealth"/> for hosts without the health probe
/// (tests, tooling): every network reads healthy.
/// </summary>
public sealed class AlwaysHealthyBlockchainNetworks : IBlockchainNetworkHealth
{
    public static readonly AlwaysHealthyBlockchainNetworks Instance = new();

    public bool IsDegraded(BlockchainNetwork network) => false;
}
