using Skinora.Transactions.Application.PaymentAddresses;

namespace Skinora.Fraud.Tests.TestSupport;

/// <summary>
/// Test double for <see cref="IPaymentAddressAllocator"/> used by the Fraud
/// review tests (WP4b — <c>FraudFlagService.ApproveAsync</c> calls the allocator
/// after a FLAGGED → CREATED promotion commits). Records every inbound
/// transaction id and, by default, reports
/// <see cref="PaymentAddressAllocationStatus.Created"/>. Individual tests flip
/// <see cref="DefaultStatus"/> / <see cref="Throw"/> or assert on
/// <see cref="Allocations"/>.
/// </summary>
internal sealed class StubPaymentAddressAllocator : IPaymentAddressAllocator
{
    public List<Guid> Allocations { get; } = new();

    public PaymentAddressAllocationStatus DefaultStatus { get; set; }
        = PaymentAddressAllocationStatus.Created;

    public string? DefaultAddress { get; set; } = "TStubDepositAddress00000000000000000";

    /// <summary>When set, <see cref="AllocateAsync"/> throws it (sidecar-outage simulation).</summary>
    public Exception? Throw { get; set; }

    public Task<PaymentAddressAllocationResult> AllocateAsync(
        Guid transactionId, CancellationToken cancellationToken)
    {
        Allocations.Add(transactionId);
        if (Throw is not null)
            throw Throw;

        return Task.FromResult(new PaymentAddressAllocationResult(
            DefaultStatus,
            transactionId,
            DefaultAddress,
            HdWalletIndex: 0,
            ErrorMessage: null));
    }
}
