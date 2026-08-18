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
    // T130 — 02 §10.1 third row: the name of the item that actually arrived on a
    // WRONG_ITEM auto-escalation, so the admin does not have to make the
    // comparison by hand. Absent on every other dispute.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeliveredItemName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UserDescription,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? AdminId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AdminNote,
    // T131 — the recorded justification when a past ruling overrode a proven
    // delivery (06 §3.11). Absent on every other resolution.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResolutionOverrideReason,
    // T131 — server-computed: would a BUYER_FAVOR ruling on THIS dispute have
    // to carry an override reason? Sent so the admin screen can ask for it up
    // front instead of discovering the rule from a rejected submission. The
    // rule has one home (the service); the client renders the answer.
    bool BuyerFavorRequiresOverride,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? ResolvedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    AdminDisputeTransactionDto Transaction);

// ---------- AD29 — POST /admin/disputes/:id/resolve (07 §9.x) ----------

/// <summary>Request body for <c>POST /admin/disputes/:id/resolve</c>.</summary>
/// <param name="Outcome">Which party the admin rules for.</param>
/// <param name="AdminNote">Required, 1..2000 — the case note (06 §3.11).</param>
/// <param name="OverrideReason">
/// T131 — required only when the ruling overrides a delivery the platform
/// already proved (03 §6.4). Ignored otherwise: it is not a second note, and
/// storing one where there was nothing to override would make the column
/// useless as the marker of an exception.
/// </param>
public sealed record AdminResolveDisputeRequest(
    DisputeResolutionOutcome Outcome,
    string? AdminNote,
    string? OverrideReason = null);

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
