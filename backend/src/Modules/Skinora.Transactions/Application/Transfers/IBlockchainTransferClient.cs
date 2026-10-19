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
    /// determine whether it has reached its network's finality (T148 —
    /// BlockchainNetworkPolicy: TRON 20 blocks, Ethereum `finalized`)
    /// (05 §3.3). Used by <c>OutgoingTransferConfirmationJob</c>.
    /// </summary>
    Task<TransferStatusResult> GetStatusAsync(
        string txHash,
        CancellationToken cancellationToken);

    /// <summary>
    /// T158 (g) — the status read with what the row says the transfer is
    /// (<see cref="TransferStatusExpectation"/>). On a network whose service
    /// verifies a hash its journal does not hold (Ethereum,
    /// <c>BlockchainNetworkPolicy.VerifiesUnjournalledTransferStatus</c>) the
    /// expectation travels with the request; the service answers
    /// <c>UNKNOWN</c> (<see cref="TransferStatusOutcome.Unverified"/>) when the
    /// receipt's Transfer log does not match it. Other implementations ignore it.
    /// </summary>
    Task<TransferStatusResult> GetStatusAsync(
        string txHash,
        TransferStatusExpectation? expected,
        CancellationToken cancellationToken) =>
        GetStatusAsync(txHash, cancellationToken);
}

/// <summary>
/// T158 (g) — the transfer a row expects on chain: the token contract, the
/// sender (deposit or hot wallet), the recipient and the exact amount
/// (08 §3.7.6 "Kesinleşme").
/// </summary>
public sealed record TransferStatusExpectation(
    string Contract,
    string FromAddress,
    string ToAddress,
    decimal Amount);

/// <param name="FeeCapUsd">
/// T152 — the fee cap of the row's network in USD
/// (<c>blockchain.ethereum.fee_cap_usd_per_transfer</c>, 08 §3.7.7) for a
/// SELLER_PAYOUT, a SWEEP and every refund type (refunds are capped too —
/// yönetici kararı 2026-10-18); null for a network without one (TRON). The
/// Ethereum service converts it with the live ETH price into the most the
/// signed transaction may cost (<c>gasLimit × maxFeePerGas</c>, per
/// transaction — yönetici kararı, T152 doğrulaması) and refuses to sign above
/// it (the send waits: <see cref="TransferBroadcastStatus.Deferred"/>).
/// </param>
/// <param name="FromHotWallet">
/// T158 (f) — a refund leaving the network's hot wallet (its deposit was swept,
/// 08 §3.7.6 "Kaynaklar"): the body says <c>source: "HOT_WALLET"</c> and names
/// no deposit. Only on a network whose policy allows it (Ethereum).
/// </param>
public sealed record TransferBroadcastRequest(
    Guid BlockchainTransactionId,
    BlockchainTransactionType Type,
    StablecoinType Token,
    decimal Amount,
    string ToAddress,
    int? DepositIndex,
    string? DepositAddress,
    decimal? FeeCapUsd = null,
    bool FromHotWallet = false);

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

    /// <summary>
    /// T152 — the service refused to sign <i>for now</i> and nothing was signed:
    /// the network fee is above the platform's cap, no ETH price is available,
    /// or the sender has no ETH for gas (08 §3.7.7). The row stays PENDING
    /// without spending a retry and is asked again next minute; the 3-hour wait
    /// alarm is the service's (<c>ethereum-transfer-wait</c>).
    /// </summary>
    Deferred,
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
/// <param name="TxHash">
/// T152 — the hash the service reports for the transfer. On Ethereum a fee bump
/// re-signs the same nonce, so the transaction that enters the chain can carry
/// another hash than the one the row stored at broadcast; the confirmation job
/// records the reported one. Null when the service did not send one.
/// </param>
/// <param name="RealizedFeeWei">
/// T154 — Ethereum: what the transfer paid, in wei (receipt
/// <c>gasUsed × effectiveGasPrice</c>), parsed exactly from the service's
/// <c>realizedFeeWei</c> decimal string. NULL for TRON, and when the service
/// did not report a well-formed value.
/// </param>
public sealed record TransferStatusResult(
    TransferStatusOutcome Outcome,
    long? BlockNumber,
    int? Confirmations,
    string? ContractRet,
    string? ErrorMessage,
    long? RealizedFeeSun = null,
    long? EnergyUsageTotal = null,
    long? OriginEnergyUsage = null,
    string? TxHash = null,
    decimal? RealizedFeeWei = null);

public enum TransferStatusOutcome
{
    /// <summary>Tx final by its network's rule (TRON `confirmations &gt;= 20`, Ethereum `finalized`) and on-chain result is SUCCESS.</summary>
    Confirmed,

    /// <summary>Tx solidified but the contract reverted/failed.</summary>
    Failed,

    /// <summary>Tx is broadcast but not yet finalized — keep polling.</summary>
    Pending,

    /// <summary>Sidecar unreachable / 5xx — caller retries on next tick.</summary>
    Unavailable,

    /// <summary>
    /// T158 (g) — final on chain, but the service could not tie the receipt to
    /// the row: a hash its journal does not hold whose Transfer log does not
    /// match the expectation (contractRet <c>UNKNOWN</c>). Fail closed: the row
    /// is neither CONFIRMED nor FAILED; it stays DETECTED and the service's
    /// <c>ethereum-transfer-status-unverified</c> alarm fires (DEPLOY_RUNBOOK §J).
    /// </summary>
    Unverified,

    /// <summary>
    /// T152 — the service answered about THIS transfer with a 4xx (unknown hash,
    /// nonce conflict, malformed hash). Row-specific: the confirmation job keeps
    /// asking about the network's other rows in the same run (only transport
    /// failures and 5xx end a network's turn).
    /// </summary>
    Rejected,
}
