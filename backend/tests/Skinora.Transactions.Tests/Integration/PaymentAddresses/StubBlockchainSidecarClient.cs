using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.Transactions.Tests.Integration.PaymentAddresses;

/// <summary>
/// Programmable <see cref="IBlockchainSidecarClient"/> stub. By default each
/// call returns a deterministic synthetic Tron address derived from the
/// requested index; queued responses override that default to drive specific
/// branches (NotConfigured, Unavailable, deliberate duplicate addresses for
/// UNIQUE-collision retry scenarios).
/// </summary>
internal sealed class StubBlockchainSidecarClient : IBlockchainSidecarClient
{
    public Queue<BlockchainSidecarDeriveResult> Responses { get; } = new();
    public List<(int Index, Guid TransactionId)> Calls { get; } = new();

    public Queue<BlockchainSidecarStatus> PostCancelStartResponses { get; } = new();
    public List<PostCancelMonitorStartRequest> PostCancelStartCalls { get; } = new();
    public Queue<BlockchainSidecarStatus> PostCancelStopResponses { get; } = new();
    public List<string> PostCancelStopCalls { get; } = new();

    public Task<BlockchainSidecarDeriveResult> DeriveAddressAsync(
        int index, Guid transactionId, CancellationToken cancellationToken)
    {
        Calls.Add((index, transactionId));
        var response = Responses.Count > 0
            ? Responses.Dequeue()
            : new BlockchainSidecarDeriveResult(
                BlockchainSidecarStatus.Success,
                DeterministicAddress(index),
                $"m/44'/195'/0'/0/{index}");
        return Task.FromResult(response);
    }

    public Queue<BlockchainSidecarStatus> MonitorStartResponses { get; } = new();
    public List<PaymentMonitorStartRequest> MonitorStartCalls { get; } = new();
    public Queue<BlockchainSidecarStatus> MonitorStopResponses { get; } = new();
    public List<string> MonitorStopCalls { get; } = new();

    /// <summary>
    /// Runs inside <see cref="StartMonitoringAsync"/>, before it returns. Lets a
    /// test commit a state change (a cancel handover) at the one instant the
    /// reconciler is mid-loop and its candidate snapshot has gone stale.
    /// </summary>
    public Func<PaymentMonitorStartRequest, Task>? OnMonitorStart { get; set; }

    public async Task<BlockchainSidecarStatus> StartMonitoringAsync(
        PaymentMonitorStartRequest request, CancellationToken cancellationToken)
    {
        MonitorStartCalls.Add(request);
        if (OnMonitorStart is not null) await OnMonitorStart(request);
        return MonitorStartResponses.Count > 0
            ? MonitorStartResponses.Dequeue()
            : BlockchainSidecarStatus.Success;
    }

    /// <summary>Twin of <see cref="OnMonitorStart"/> for the disarm branch.</summary>
    public Func<string, Task>? OnMonitorStop { get; set; }

    public async Task<BlockchainSidecarStatus> StopMonitoringAsync(
        string address, CancellationToken cancellationToken)
    {
        MonitorStopCalls.Add(address);
        if (OnMonitorStop is not null) await OnMonitorStop(address);
        return MonitorStopResponses.Count > 0
            ? MonitorStopResponses.Dequeue()
            : BlockchainSidecarStatus.Success;
    }

    public Task<BlockchainSidecarStatus> StartPostCancelMonitoringAsync(
        PostCancelMonitorStartRequest request, CancellationToken cancellationToken)
    {
        PostCancelStartCalls.Add(request);
        var response = PostCancelStartResponses.Count > 0
            ? PostCancelStartResponses.Dequeue()
            : BlockchainSidecarStatus.Success;
        return Task.FromResult(response);
    }

    public Task<BlockchainSidecarStatus> StopPostCancelMonitoringAsync(
        string address, CancellationToken cancellationToken)
    {
        PostCancelStopCalls.Add(address);
        var response = PostCancelStopResponses.Count > 0
            ? PostCancelStopResponses.Dequeue()
            : BlockchainSidecarStatus.Success;
        return Task.FromResult(response);
    }

    public Queue<BlockchainSidecarBalancesResult> WalletBalanceResponses { get; } = new();
    public List<IReadOnlyList<string>> WalletBalanceCalls { get; } = new();

    public Task<BlockchainSidecarBalancesResult> GetWalletBalancesAsync(
        IReadOnlyList<string> addresses, CancellationToken cancellationToken)
    {
        WalletBalanceCalls.Add(addresses);
        var response = WalletBalanceResponses.Count > 0
            ? WalletBalanceResponses.Dequeue()
            : new BlockchainSidecarBalancesResult(
                BlockchainSidecarStatus.Success,
                BlockNumber: 0,
                Balances: Array.Empty<BlockchainSidecarAddressBalances>());
        return Task.FromResult(response);
    }

    public Queue<BlockchainSidecarTransferResult> HotToColdResponses { get; } = new();
    public List<HotToColdTransferRequest> HotToColdCalls { get; } = new();

    public Task<BlockchainSidecarTransferResult> SendHotToColdTransferAsync(
        HotToColdTransferRequest request, CancellationToken cancellationToken)
    {
        HotToColdCalls.Add(request);
        var response = HotToColdResponses.Count > 0
            ? HotToColdResponses.Dequeue()
            : new BlockchainSidecarTransferResult(
                BlockchainSidecarStatus.Success,
                TxHash: DeterministicTxHash(HotToColdCalls.Count));
        return Task.FromResult(response);
    }

    public static string DeterministicTxHash(int seed) =>
        $"0xstub{seed:x}".PadRight(64, '0');

    public static string DeterministicAddress(int index)
        // Synthetic 34-char Tron-like base58 address. Real validation against
        // the live derivation is covered by the sidecar's own Vitest suite —
        // here we only need the UNIQUE constraint to behave correctly.
        => $"TStubAddress{index:D22}";
}
