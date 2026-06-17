using System.Text.Json.Serialization;
using Skinora.Shared.Enums;

namespace Skinora.Disputes.Application.Admin;

// ---------- AD27 — GET /admin/disputes (07 §9.x) ----------

/// <summary>
/// Query for the admin dispute queue. Defaults to the ESCALATED bucket — the
/// dead-end disputes waiting for an admin decision (WP5 / T58-AdminDisputeQueue).
/// </summary>
public sealed record AdminDisputeListQuery(
    DisputeStatus? Status,
    DisputeType? Type,
    int Page,
    int PageSize);

/// <summary>Party summary (buyer / seller) on an admin dispute row.</summary>
public sealed record AdminDisputePartyDto(
    Guid UserId,
    string? SteamId,
    string DisplayName);

/// <summary>One row of the admin dispute queue (07 §9.x).</summary>
public sealed record AdminDisputeListItemDto(
    Guid Id,
    Guid TransactionId,
    DisputeType Type,
    DisputeStatus Status,
    string ItemName,
    TransactionStatus TransactionStatus,
    AdminDisputePartyDto OpenedBy,
    DateTime CreatedAt);

// ---------- AD28 — GET /admin/disputes/:id (07 §9.x) ----------

/// <summary>Transaction summary surfaced alongside the dispute detail.</summary>
public sealed record AdminDisputeTransactionDto(
    Guid Id,
    TransactionStatus Status,
    string ItemName,
    decimal Price,
    StablecoinType Stablecoin,
    bool IsOnHold,
    bool HasActiveDispute,
    AdminDisputePartyDto Seller,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AdminDisputePartyDto? Buyer);

/// <summary>Full admin view of a dispute (07 §9.x).</summary>
public sealed record AdminDisputeDetailDto(
    Guid Id,
    DisputeType Type,
    DisputeStatus Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SystemCheckResult,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UserDescription,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? AdminId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AdminNote,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? ResolvedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    AdminDisputeTransactionDto Transaction);

// ---------- AD29 — POST /admin/disputes/:id/resolve (07 §9.x) ----------

/// <summary>Request body for <c>POST /admin/disputes/:id/resolve</c>.</summary>
public sealed record AdminResolveDisputeRequest(
    DisputeResolutionOutcome Outcome,
    string? AdminNote);

/// <summary>Response body for a successful resolution.</summary>
public sealed record AdminResolveDisputeResponse(
    Guid Id,
    DisputeStatus Status,
    TransactionStatus TransactionStatus,
    DateTime ResolvedAt,
    bool BuyerRefunded);

/// <summary>
/// Outcome of <see cref="IAdminDisputeService.ResolveAsync"/>. The controller
/// maps <see cref="Status"/> to the HTTP response.
/// </summary>
public sealed record AdminResolveDisputeOutcome(
    AdminResolveDisputeStatus Status,
    AdminResolveDisputeResponse? Body,
    string? ErrorCode,
    string? ErrorMessage);

public enum AdminResolveDisputeStatus
{
    Resolved,
    NotFound,
    NotEscalated,
    TransactionOnHold,
    InvalidStateTransition,
    ValidationFailed,
}
