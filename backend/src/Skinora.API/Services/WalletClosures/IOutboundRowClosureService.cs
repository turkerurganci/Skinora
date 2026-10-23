using Skinora.Shared.Enums;

namespace Skinora.API.Services.WalletClosures;

/// <summary>
/// Two-person closure of an outbound row with an on-chain hash (T157 — 07
/// §9.32; T172a — TRON and the post-restore FAILED row). See
/// <see cref="OutboundRowClosureService"/>.
/// </summary>
public interface IOutboundRowClosureService
{
    /// <summary>
    /// Outbound rows a closure could still be proposed for: no hash and either
    /// <c>PENDING</c> or — T172a — <c>FAILED</c> with the
    /// <c>POST_RESTORE_DUPLICATE</c> note. <paramref name="network"/> null = both networks.
    /// </summary>
    Task<IReadOnlyList<ClosureCandidateDto>> ListCandidatesAsync(
        BlockchainNetwork? network, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClosureDto>> ListAsync(
        OutboundRowClosureStatus? status, BlockchainNetwork? network, CancellationToken cancellationToken);

    Task<ClosureResult> ProposeAsync(
        Guid adminId, Guid blockchainTransactionId, string? txHash, string? reason,
        string? ipAddress, CancellationToken cancellationToken);

    /// <param name="confirmTxHash">The approver retypes the hash they verified.</param>
    Task<ClosureResult> ApproveAsync(
        Guid adminId, Guid closureId, string? confirmTxHash,
        string? ipAddress, CancellationToken cancellationToken);

    Task<ClosureResult> RejectAsync(
        Guid adminId, Guid closureId, string? note,
        string? ipAddress, CancellationToken cancellationToken);
}

/// <summary>An open outbound row (07 §9.32 candidates).</summary>
/// <param name="RowStatus">
/// <c>PENDING</c>, or <c>FAILED</c> for a row closed by hand after a restore
/// (<c>POST_RESTORE_DUPLICATE</c>) that the closure brings back to the real
/// payment (T172a).
/// </param>
/// <param name="Token">The stablecoin the transaction expected (06 §3.8 <c>Token</c>).</param>
/// <param name="ChainToken">
/// The stablecoin the row moves on chain — differs from <paramref name="Token"/>
/// for a wrong-token refund (the token that arrived); null when it cannot be
/// resolved (the tool then refuses the row). T157 düzeltme turu.
/// </param>
public sealed record ClosureCandidateDto(
    Guid BlockchainTransactionId,
    Guid TransactionId,
    BlockchainNetwork Network,
    BlockchainTransactionStatus RowStatus,
    BlockchainTransactionType Type,
    StablecoinType Token,
    StablecoinType? ChainToken,
    decimal Amount,
    string FromAddress,
    string ToAddress,
    int RetryCount,
    DateTime CreatedAt,
    bool HasLiveClosure);

/// <summary>One closure with the row it concerns (07 §9.32).</summary>
public sealed record ClosureDto(
    Guid Id,
    OutboundRowClosureStatus Status,
    BlockchainNetwork Network,
    Guid BlockchainTransactionId,
    Guid TransactionId,
    BlockchainTransactionType RowType,
    StablecoinType Token,
    StablecoinType? ChainToken,
    decimal Amount,
    string FromAddress,
    string ToAddress,
    BlockchainTransactionStatus RowStatus,
    string TxHash,
    string Reason,
    Guid ProposedByAdminId,
    string ProposedByName,
    DateTime ProposedAt,
    Guid? DecidedByAdminId,
    string? DecidedByName,
    DateTime? DecidedAt,
    string? DecisionNote);

public enum ClosureError
{
    Validation,
    InvalidHash,
    RowNotFound,
    RowNotOutbound,
    RowNotOpen,
    ClosureNotFound,
    ClosureNotOpen,
    HashAlreadyBound,
    OpenClosureExists,
    SelfApproval,
    HashMismatch,
    HashNotFinal,
    HashNotSuccessful,
    HashNotFound,
    HashDoesNotMatchRow,
    ChainUnavailable,

    /// <summary>
    /// T157 düzeltme turu — the row's on-chain transfer (token, sender) cannot
    /// be resolved, so it cannot be verified (fail closed).
    /// </summary>
    RowTransferUnresolved,

    /// <summary>
    /// T157 düzeltme turu, yönetici kararı (⚠ PARA) — another open
    /// (PENDING/DETECTED) outbound row on the network has the same recipient,
    /// token and amount: a chain hash cannot tell which one it paid.
    /// </summary>
    AmbiguousRowMatch,
    Conflict,
}

public sealed record ClosureResult(ClosureDto? Value, ClosureError? Error, string? Message)
{
    public static ClosureResult Ok(ClosureDto value) => new(value, null, null);

    public static ClosureResult Fail(ClosureError error, string message) => new(null, error, message);
}
