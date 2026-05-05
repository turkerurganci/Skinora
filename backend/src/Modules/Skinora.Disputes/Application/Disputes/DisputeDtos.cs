using System.Text.Json.Serialization;
using Skinora.Shared.Enums;

namespace Skinora.Disputes.Application.Disputes;

// ---------- POST /transactions/:id/disputes (07 §7.8) ----------

/// <summary>Request body for <c>POST /transactions/:id/disputes</c>.</summary>
public sealed record OpenDisputeRequest(DisputeType Type);

/// <summary>Auto-check section returned inside the open-dispute response (07 §7.8).</summary>
public sealed record AutoCheckResultDto(
    bool Resolved,
    string Message,
    bool CanSubmitTxHash,
    bool CanEscalate);

/// <summary>Response body for <c>POST /transactions/:id/disputes</c>.</summary>
public sealed record OpenDisputeResponse(
    Guid Id,
    DisputeType Type,
    DisputeStatus Status,
    AutoCheckResultDto AutoCheckResult,
    DateTime CreatedAt);

/// <summary>
/// Outcome of <see cref="IDisputeService.OpenAsync"/>. The controller pattern
/// matches on <see cref="Status"/> to produce 200 / 4xx responses without
/// leaking implementation details.
/// </summary>
public sealed record OpenDisputeOutcome(
    OpenDisputeStatus Status,
    OpenDisputeResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

public enum OpenDisputeStatus
{
    Opened,
    NotFound,
    NotBuyer,
    InvalidStateTransition,
    DuplicateDispute,
    ValidationFailed,
}

// ---------- POST /transactions/:id/disputes/:disputeId/submit-txhash (07 §7.9) ----------

/// <summary>Request body for <c>POST /transactions/:id/disputes/:disputeId/submit-txhash</c>.</summary>
public sealed record SubmitTxHashRequest(string TxHash);

/// <summary>Inner payload returned by submit-txhash.</summary>
public sealed record TxHashCheckResultDto(bool Resolved, string Message);

/// <summary>Response body for <c>POST /transactions/:id/disputes/:disputeId/submit-txhash</c>.</summary>
public sealed record SubmitTxHashResponse(
    [property: JsonPropertyName("checkResult")] TxHashCheckResultDto CheckResult);

/// <summary>
/// Outcome of <see cref="IDisputeService.SubmitTxHashAsync"/>.
/// </summary>
public sealed record SubmitTxHashOutcome(
    SubmitTxHashStatus Status,
    SubmitTxHashResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

public enum SubmitTxHashStatus
{
    Processed,
    NotFound,
    NotBuyer,
    NotPaymentDispute,
    DisputeClosed,
    ValidationFailed,
}

// ---------- POST /transactions/:id/disputes/:disputeId/escalate (07 §7.10) ----------

/// <summary>Request body for <c>POST /transactions/:id/disputes/:disputeId/escalate</c>.</summary>
public sealed record EscalateDisputeRequest(string Detail);

/// <summary>Response body for <c>POST /transactions/:id/disputes/:disputeId/escalate</c>.</summary>
public sealed record EscalateDisputeResponse(
    DisputeStatus Status,
    DateTime EscalatedAt,
    string Message);

/// <summary>
/// Outcome of <see cref="IDisputeService.EscalateAsync"/>.
/// </summary>
public sealed record EscalateDisputeOutcome(
    EscalateDisputeStatus Status,
    EscalateDisputeResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

public enum EscalateDisputeStatus
{
    Escalated,
    NotFound,
    NotBuyer,
    AlreadyEscalated,
    DisputeClosed,
    ValidationFailed,
}
