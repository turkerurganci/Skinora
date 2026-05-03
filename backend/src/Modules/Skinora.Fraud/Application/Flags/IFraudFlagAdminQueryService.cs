using Skinora.Shared.Enums;

namespace Skinora.Fraud.Application.Flags;

/// <summary>
/// Read port for the admin flag review queue (T54 — 07 §9.2 / §9.3,
/// 03 §8.2). Results are <c>AsNoTracking</c> projections; <c>flagDetail</c>
/// payloads are deserialised from <c>FraudFlag.Details</c> JSON.
/// </summary>
public interface IFraudFlagAdminQueryService
{
    /// <summary>AD2 — paged list with <c>pendingCount</c> badge.</summary>
    Task<FraudFlagListResponse> ListAsync(
        FraudFlagListQuery query, CancellationToken cancellationToken);

    /// <summary>AD3 — single flag detail. Returns <c>null</c> when the id is unknown.</summary>
    Task<FraudFlagDetailDto?> GetDetailAsync(
        Guid id, CancellationToken cancellationToken);
}

/// <summary>Filter inputs for <see cref="IFraudFlagAdminQueryService.ListAsync"/>.</summary>
public sealed record FraudFlagListQuery(
    FraudFlagType? Type,
    ReviewStatus? ReviewStatus,
    DateTime? DateFrom,
    DateTime? DateTo,
    string? SortBy,
    string? SortOrder,
    int Page,
    int PageSize);
