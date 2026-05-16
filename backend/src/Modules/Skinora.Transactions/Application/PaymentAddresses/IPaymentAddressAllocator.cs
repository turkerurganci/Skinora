namespace Skinora.Transactions.Application.PaymentAddresses;

/// <summary>
/// Allocates a new <c>PaymentAddress</c> row for the given transaction by
/// (1) reading the next monotonic <c>HdWalletIndex</c>, (2) asking the
/// blockchain sidecar to derive the address, (3) inserting under the
/// <c>HdWalletIndex</c>/<c>Address</c> UNIQUE constraints with retry on
/// concurrent collisions (08 §3.2 atomicity guarantee).
/// </summary>
public interface IPaymentAddressAllocator
{
    Task<PaymentAddressAllocationResult> AllocateAsync(
        Guid transactionId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated outcome — the controller and the Hangfire fallback job both
/// pattern-match on <see cref="Status"/> to decide whether to surface, retry,
/// or wait.
/// </summary>
public sealed record PaymentAddressAllocationResult(
    PaymentAddressAllocationStatus Status,
    Guid TransactionId,
    string? Address,
    int? HdWalletIndex,
    string? ErrorMessage);

public enum PaymentAddressAllocationStatus
{
    /// <summary>A new <c>PaymentAddress</c> row was inserted.</summary>
    Created,

    /// <summary>An address already existed for this transaction (idempotent).</summary>
    AlreadyExisted,

    /// <summary>The transaction does not exist or is soft-deleted.</summary>
    TransactionNotFound,

    /// <summary>The transaction is in a state that does not need a payment address.</summary>
    TransactionIneligible,

    /// <summary>Sidecar returned <c>HD_WALLET_NOT_CONFIGURED</c> (503).</summary>
    SidecarNotConfigured,

    /// <summary>Sidecar was unreachable or returned a transport error.</summary>
    SidecarUnavailable,

    /// <summary>
    /// All retry attempts hit a UNIQUE collision on
    /// <c>HdWalletIndex</c>/<c>Address</c>. Indicates either a bug in the
    /// allocator loop or extreme contention.
    /// </summary>
    ExhaustedRetries,
}
