using Skinora.Shared.Enums;

namespace Skinora.Transactions.Application.Transfers;

/// <summary>
/// HTTP port over the blockchain sidecar's outbound-transfer endpoints
/// (08 §3.1, §3.3, T73). The dispatcher hands the sidecar a
/// <see cref="TransferBroadcastRequest"/> and persists whatever txid comes
/// back; retry, admin alerting, and confirmation polling stay on this side
/// of the wire.
///
/// <para>
/// The sidecar exposes three flow-specific endpoints (payout / refund /
/// sweep) plus a status lookup; this port collapses them into a single
/// broadcast call discriminated by <see cref="BlockchainTransactionType"/>
/// so the dispatcher does not have to switch on the type itself.
/// </para>
/// </summary>
public interface IBlockchainTransferClient
{
    Task<TransferBroadcastResult> BroadcastAsync(
        TransferBroadcastRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Look up a previously broadcast txid against the solidity node to
    /// determine whether it has reached the 20-block confirmation threshold
    /// (05 §3.3). Used by <c>OutgoingTransferConfirmationJob</c>.
    /// </summary>
    Task<TransferStatusResult> GetStatusAsync(
        string txHash,
        CancellationToken cancellationToken);
}

public sealed record TransferBroadcastRequest(
    Guid BlockchainTransactionId,
    BlockchainTransactionType Type,
    StablecoinType Token,
    decimal Amount,
    string ToAddress,
    int? DepositIndex,
    string? DepositAddress);

public sealed record TransferBroadcastResult(
    TransferBroadcastStatus Status,
    string? TxHash,
    string? ErrorCode,
    string? ErrorMessage);

public enum TransferBroadcastStatus
{
    /// <summary>Sidecar broadcast the transaction; <c>TxHash</c> is non-null.</summary>
    Success,

    /// <summary>Sidecar returned 400 — bad request, do not retry.</summary>
    InvalidRequest,

    /// <summary>Sidecar 5xx / network / build / broadcast transient failure — caller retries.</summary>
    TransientFailure,
}

/// <param name="RealizedFeeSun">
/// TRX actually burned by the transfer, in SUN, straight from the chain
/// receipt. NULL until the sidecar can read it; legitimately <c>0</c> when the
/// transfer cost the sender nothing.
/// </param>
/// <param name="EnergyUsageTotal">Total Energy the call consumed.</param>
/// <param name="OriginEnergyUsage">
/// Energy the contract owner absorbed on the sender's behalf. A zero fee next
/// to a non-zero value here means the CONTRACT paid — not that the transfer
/// was free to arrange.
/// </param>
public sealed record TransferStatusResult(
    TransferStatusOutcome Outcome,
    long? BlockNumber,
    int? Confirmations,
    string? ContractRet,
    string? ErrorMessage,
    long? RealizedFeeSun = null,
    long? EnergyUsageTotal = null,
    long? OriginEnergyUsage = null);

public enum TransferStatusOutcome
{
    /// <summary>Tx confirmed (`confirmations &gt;= 20`) and on-chain result is SUCCESS.</summary>
    Confirmed,

    /// <summary>Tx solidified but the contract reverted/failed.</summary>
    Failed,

    /// <summary>Tx is broadcast but not yet finalized — keep polling.</summary>
    Pending,

    /// <summary>Sidecar unreachable / 5xx — caller retries on next tick.</summary>
    Unavailable,
}
