using Skinora.Shared.Enums;

namespace Skinora.API.Services.WalletClosures;

/// <summary>
/// Two-person closure of an Ethereum outbound row with an on-chain hash
/// (T157 — 07 §9.32). See <see cref="OutboundRowClosureService"/>.
/// </summary>
public interface IOutboundRowClosureService
{
    /// <summary>Ethereum outbound rows a closure could still be proposed for (PENDING, no hash).</summary>
    Task<IReadOnlyList<ClosureCandidateDto>> ListCandidatesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ClosureDto>> ListAsync(
        OutboundRowClosureStatus? status, CancellationToken cancellationToken);

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

/// <summary>An open Ethereum outbound row (07 §9.32 candidates).</summary>
public sealed record ClosureCandidateDto(
    Guid BlockchainTransactionId,
    Guid TransactionId,
    BlockchainTransactionType Type,
    StablecoinType Token,
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
    Guid BlockchainTransactionId,
    Guid TransactionId,
    BlockchainTransactionType RowType,
    StablecoinType Token,
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
    RowNotEthereum,
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
    Conflict,
}

public sealed record ClosureResult(ClosureDto? Value, ClosureError? Error, string? Message)
{
    public static ClosureResult Ok(ClosureDto value) => new(value, null, null);

    public static ClosureResult Fail(ClosureError error, string message) => new(null, error, message);
}
