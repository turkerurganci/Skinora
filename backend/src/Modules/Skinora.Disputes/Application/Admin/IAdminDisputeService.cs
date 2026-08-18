using Skinora.Shared.Models;

namespace Skinora.Disputes.Application.Admin;

/// <summary>
/// WP5 / T58 — admin dispute resolution surface. Closes the ESCALATED dead-end
/// (02 §10.4, 03 §6.4): admins list escalated disputes, inspect one, and resolve
/// it in favor of the seller (uphold → payout proceeds) or the buyer (unwind →
/// REFUNDED + payment refund; there is no item leg — 02 §3.2). All resolution
/// side effects (Dispute terminal status, AdminId/AdminNote/ResolvedAt, the
/// T131 override reason, transaction transition, payment refund event, audit,
/// notification) land inside a single
/// <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync"/>.
/// </summary>
/// <remarks>
/// Declared in the Disputes module but implemented in the API composition layer
/// (<c>Skinora.API.Services.AdminDisputeService</c>) — same port/adapter pattern
/// as <see cref="Skinora.Transactions.Application.Admin.IAdminTransactionQueryService"/>,
/// because the resolution orchestrates Disputes + Transactions + Platform (audit)
/// across module boundaries.
/// </remarks>
public interface IAdminDisputeService
{
    /// <summary>AD27 — <c>GET /admin/disputes</c>. Defaults to the ESCALATED bucket.</summary>
    Task<PagedResult<AdminDisputeListItemDto>> ListAsync(
        AdminDisputeListQuery query,
        CancellationToken cancellationToken);

    /// <summary>AD28 — <c>GET /admin/disputes/:id</c>. Returns null when not found.</summary>
    Task<AdminDisputeDetailDto?> GetAsync(
        Guid disputeId,
        CancellationToken cancellationToken);

    /// <summary>AD29 — <c>POST /admin/disputes/:id/resolve</c>.</summary>
    Task<AdminResolveDisputeOutcome> ResolveAsync(
        Guid adminUserId,
        Guid disputeId,
        AdminResolveDisputeRequest request,
        string? ipAddress,
        CancellationToken cancellationToken);
}
