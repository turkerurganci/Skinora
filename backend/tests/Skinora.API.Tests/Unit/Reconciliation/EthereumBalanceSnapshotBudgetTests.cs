using Skinora.Shared.Blockchain;
using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.API.Tests.Unit.Reconciliation;

/// <summary>
/// T158 doğrulaması, bulgu (i) — one Ethereum balance chunk must finish inside
/// the budget its request runs under. The Ethereum service paces every balance
/// read through ONE rate pacer it shares with the payment scanner and its own
/// hourly reconciliation; the T158 chunk of 20 addresses ran on the 10-s wallet
/// client. The chunk and the budget live in different places
/// (<see cref="BlockchainNetworkPolicy"/>, <see cref="BlockchainSidecarOptions"/>),
/// so the arithmetic that ties them is pinned here: growing the chunk or
/// shrinking the budget turns this red.
/// </summary>
/// <remarks>
/// <para>
/// The CU constants mirror <c>sidecar-ethereum/src/reconcile/RatePacer.ts</c>
/// <c>CU_COST</c> (<c>eth_getLogs</c> 60, <c>eth_call</c> 26,
/// <c>eth_getBalance</c> 20) and the pacer rate mirrors
/// <c>ETHEREUM_RECONCILIATION_CU_PER_SECOND</c>'s default 200
/// (<c>sidecar-ethereum/src/config/index.ts</c>). A balance read is one
/// <c>eth_getBalance</c> (ETH) plus one <c>eth_call</c> per configured token
/// (USDT + USDC — <c>BalanceReader.costPerAddress</c>). If the service's costs or
/// rate change, change them here and re-check the chunk.
/// </para>
/// <para>
/// The pacer interleaves per CALL: each of an address's calls waits at most one
/// scanner call (the largest is <c>eth_getLogs</c>) and one hourly-reconciliation
/// call (the largest is <c>eth_call</c>) ahead of it. Worst case per address
/// 3 × (60 + 26) + 72 = 330 CU = 1.65 s; measured on the service with the
/// scanner only, a 10-address request took 12.5 s.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class EthereumBalanceSnapshotBudgetTests
{
    /// <summary>sidecar-ethereum <c>CU_COST.eth_getBalance</c>.</summary>
    private const int EthGetBalanceCu = 20;

    /// <summary>sidecar-ethereum <c>CU_COST.eth_call</c>.</summary>
    private const int EthCallCu = 26;

    /// <summary>sidecar-ethereum <c>CU_COST.eth_getLogs</c> — the scanner's largest call.</summary>
    private const int EthGetLogsCu = 60;

    /// <summary>The service's hourly reconciliation's largest call (<c>eth_call</c>).</summary>
    private const int ReconciliationLargestCallCu = EthCallCu;

    /// <summary>USDT + USDC — one <c>eth_call</c> each per address.</summary>
    private const int TokensPerAddress = 2;

    /// <summary>sidecar-ethereum <c>ETHEREUM_RECONCILIATION_CU_PER_SECOND</c> default.</summary>
    private const int PacerCuPerSecond = 200;

    [Fact]
    public void T158i_An_Ethereum_Balance_Chunk_Fits_The_Chain_Read_Budget_With_The_Pacer_Shared()
    {
        var chunk = BlockchainNetworkPolicy.Ethereum.BalanceSnapshotChunkSize;
        Assert.Equal(10, chunk);

        const int callsPerAddress = 1 + TokensPerAddress;
        const int ownCu = EthGetBalanceCu + (TokensPerAddress * EthCallCu);
        const int worstCuPerAddress = (callsPerAddress * (EthGetLogsCu + ReconciliationLargestCallCu)) + ownCu;
        Assert.Equal(72, ownCu);
        Assert.Equal(330, worstCuPerAddress);

        var worstCase = WorstCase(chunk!.Value, worstCuPerAddress);
        var defaults = new BlockchainSidecarOptions();
        var chainReadBudget = defaults.ResolveChainReadTimeout();

        Assert.Equal(TimeSpan.FromSeconds(16.5), worstCase);
        Assert.Equal(TimeSpan.FromSeconds(30), chainReadBudget);
        Assert.True(worstCase < chainReadBudget,
            $"A {chunk}-address chunk needs up to {worstCase.TotalSeconds:0.##} s; the chain-read budget is {chainReadBudget.TotalSeconds:0.##} s.");

        // Why 10 and not the T158 value: 20 addresses would not fit even the
        // chain-read budget. And why the request left the wallet/monitor
        // client: even 10 do not fit its 10 s.
        Assert.True(WorstCase(20, worstCuPerAddress) > chainReadBudget);
        Assert.True(worstCase > TimeSpan.FromSeconds(defaults.TimeoutSeconds));
    }

    private static TimeSpan WorstCase(int addresses, int worstCuPerAddress) =>
        TimeSpan.FromMilliseconds(addresses * worstCuPerAddress * 1000L / PacerCuPerSecond);
}
