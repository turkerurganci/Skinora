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

    public static string DeterministicAddress(int index)
        // Synthetic 34-char Tron-like base58 address. Real validation against
        // the live derivation is covered by the sidecar's own Vitest suite —
        // here we only need the UNIQUE constraint to behave correctly.
        => $"TStubAddress{index:D22}";
}
