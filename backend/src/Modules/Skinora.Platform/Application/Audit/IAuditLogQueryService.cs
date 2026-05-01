using Skinora.Shared.Models;

namespace Skinora.Platform.Application.Audit;

/// <summary>
/// Read side of the audit log subsystem — backs <c>GET /admin/audit-logs</c>
/// (07 §9.19, AD18). Read-only, paginated, filterable.
/// </summary>
public interface IAuditLogQueryService
{
    Task<PagedResult<AuditLogListItemDto>> ListAsync(
        AuditLogListQuery query,
        CancellationToken cancellationToken);
}
