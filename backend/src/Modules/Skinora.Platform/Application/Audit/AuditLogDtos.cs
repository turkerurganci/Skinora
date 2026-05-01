using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skinora.Platform.Application.Audit;

/// <summary>
/// One row of the AD18 audit log list (07 §9.19). <c>Action</c> is rendered
/// as the <see cref="Skinora.Shared.Enums.AuditAction"/> string name to match
/// the 07 §9.19 example payload.
/// </summary>
public sealed record AuditLogListItemDto(
    string Id,
    string Category,
    string Action,
    AuditLogParticipantDto Actor,
    AuditLogParticipantDto? Subject,
    Guid? TransactionId,
    [property: JsonPropertyName("detail")] JsonElement? Detail,
    DateTime CreatedAt);

/// <summary>
/// Actor / subject reference (07 §9.19). <c>steamId</c> is null for the
/// SYSTEM service account; the API layer surfaces <c>displayName: "System"</c>
/// in that case.
/// </summary>
public sealed record AuditLogParticipantDto(
    string? SteamId,
    string DisplayName);

/// <summary>
/// Query parameters accepted by AD18 (07 §9.19). All filters are optional;
/// missing values short-circuit to "no filter".
/// </summary>
public sealed record AuditLogListQuery(
    string? Category,
    DateTime? DateFrom,
    DateTime? DateTo,
    string? Search,
    Guid? TransactionId,
    int Page,
    int PageSize);
